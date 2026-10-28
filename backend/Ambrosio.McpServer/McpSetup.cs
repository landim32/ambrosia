using Ambrosio.DTO.Settings;
using Ambrosio.McpServer.Auth;
using Ambrosio.McpServer.Mcp;
using Ambrosio.McpServer.Mcp.Filters;
using Ambrosio.McpServer.Middlewares;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.AspNetCore.Authentication;
using ModelContextProtocol.Authentication;
using System.Security.Claims;
using System.Text.Json;

namespace Ambrosio.McpServer;

/// <summary>
/// Configuração do endpoint MCP protegido (feature 007, T018/T019/T020, R5/R6/R7/R17).
/// </summary>
public static class McpSetup
{
    /// <summary>Caminho do endpoint, fixado em <c>/mcp</c> (R7).</summary>
    public const string McpPath = "/mcp";

    /// <summary>Nome do esquema de autenticação que protege o MCP.</summary>
    public const string Scheme = "McpBearer";

    /// <summary>Esquema de cookie da sessão das telas do authorization server (T040).</summary>
    public const string AuthSessionScheme = "AmbrosioAuthSession";

    /// <summary>Nome do campo antiforgery dos formulários do AS.</summary>
    public const string AntiforgeryFieldName = "__RequestVerificationToken";

    /// <summary>
    /// Esquema registrado pelo <c>AddMcp()</c> do SDK, que é quem sabe montar o
    /// <c>WWW-Authenticate: ... resource_metadata="..."</c> da RFC 9728.
    /// </summary>
    /// <remarks>
    /// Literal deliberado, e a razão é uma pegadinha: <c>McpAuthenticationDefaults.AuthenticationScheme</c>
    /// vale <c>"Bearer"</c>, mas o <c>AddMcp()</c> registra o handler como <c>"McpAuth"</c> — a lista
    /// real de esquemas do host é <c>OpenIddict.Server.AspNetCore, McpBearer, McpAuth</c> (vista no
    /// erro do probe). Referenciar o default produz "No authentication handler is registered for the
    /// scheme 'Bearer'"; se o SDK mudar esse nome, o 401 volta a nascer sem metadata e o teste de
    /// contrato (T075) é quem avisa.
    /// </remarks>
    public const string ChallengeScheme = "McpAuth";

    /// <summary>
    /// Validação do access token + transporte do servidor, ambos sem estado.
    /// </summary>
    /// <remarks>
    /// Decisão que precisa de leitura atenta: o token é validado pela <b>chave pública local</b>, e
    /// não por <c>Authority</c> + JWKS remoto. O authorization server está no mesmo processo (R8),
    /// então <c>Authority</c> faria cada requisição do ChatGPT gerar um HTTP de retorno ao próprio
    /// host — lentidão, risco de deadlock em restart e validação dependente da borda pública. O
    /// material de chave é exatamente o mesmo que o JWKS publica, então a criptografia conferida é
    /// idêntica; <c>/auth/.well-known/jwks</c> continua servindo para qualquer validador externo.
    /// <para>
    /// Stateless não é apenas preferência de escala (R6): a partir da revisão de protocolo
    /// <c>2026-07-28</c> o Streamable HTTP não tem mais sessão nem handshake de <c>initialize</c>
    /// (SEP-2567/SEP-2575, documentado em <see cref="HttpServerSessionMode"/>), então requisições
    /// dessa revisão só podem ser atendidas sem estado. Sampling, elicitation e roots ficam
    /// indisponíveis por consequência — e nada neste feature usa os três.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddAmbrosioMcp(
        this IServiceCollection services,
        IConfiguration configuration,
        SigningKeyProvider signingKey)
    {
        var mcp = configuration.GetSection("Mcp").Get<McpSettings>() ?? new McpSettings();
        var authServer = configuration.GetSection("Auth").Get<AuthServerSettings>() ?? new AuthServerSettings();

        // Validador com a chave PÚBLICA (veio de ExportParameters(false)); a privada fica só no
        // assinador do OpenIddict.
        var validationKey = signingKey.ValidationKey;

        var authBuilder = services
            // Autentica no JwtBearer, desafia no handler do SDK. Essa separação é o que faz o 401
            // carregar `resource_metadata=`: com o desafio saindo do JwtBearer o header é só
            // "Bearer", e o ChatGPT não tem como descobrir o authorization server a partir do
            // recurso — que é exatamente o mecanismo da RFC 9728 (R5).
            // AddAuthentication não tem sobrecarga de dois esquemas; é pelo AuthenticationOptions.
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = Scheme;
                options.DefaultChallengeScheme = ChallengeScheme;
            })

            // O access token do AS: RS256, `iss` = Auth__Issuer, `aud` EXIGENTE = Mcp__ResourceUrl.
            .AddJwtBearer(Scheme, options =>
            {
                // Mesma razão do Program da API: sem isto o claim `sub` é renomeado para
                // ClaimTypes.NameIdentifier e GetUserId() passa a devolver 00000000-0000...
                options.MapInboundClaims = false;

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = authServer.Issuer,
                    ValidateAudience = true,
                    ValidAudience = mcp.ResourceUrl,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = validationKey,
                    // 1 minuto: os tokens têm 30 minutos de vida, e tolerar relógio solto atrasaria
                    // a percepção de um token revogado.
                    ClockSkew = TimeSpan.FromMinutes(1),
                    // `client_id` da credencial do ChatGPT tem de chegar sem renomeação para a
                    // auditoria (R4).
                    RoleClaimType = ClaimTypes.Role,
                    NameClaimType = "sub"
                };
            })

            // O handler do SDK acrescenta `resource_metadata` ao 401 e serve o documento de recurso
            // protegido (RFC 9728). Ele NÃO valida token: a validação é o AddJwtBearer acima.
            .AddMcp(options =>
            {
                options.ResourceMetadata = new ProtectedResourceMetadata
                {
                    Resource = mcp.ResourceUrl,
                    ResourceName = "Ambrosio",
                    ResourceDocumentation = "https://ambrosioai.site"
                };

                // Coleções somente-leitura na metadata: entram por Add, não por atribuição.
                options.ResourceMetadata.AuthorizationServers.Add(authServer.Issuer);
                foreach (var scope in ScopeSeeder.ScopeNames)
                {
                    options.ResourceMetadata.ScopesSupported.Add(scope);
                }
            });

        // Sessão das telas do AS (T040): só lembra quem entrou entre o authorize, o login e o
        // consentimento. Path=/auth e curta duração; não guarda token de API (constituição §VI,
        // emenda 3.4.0). `Secure` segue a requisição: em produção ela chega https pelo
        // X-Forwarded-Proto e o cookie sai Secure; a homologação é http e o cookie não poderia ser.
        authBuilder.AddCookie(AuthSessionScheme, options =>
        {
            options.Cookie.Name = "ambrosio.auth.session";
            options.Cookie.Path = OpenIddictSetup.AuthPathBase;
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.ExpireTimeSpan = TimeSpan.FromMinutes(20);
            options.SlidingExpiration = false;
        });

        services.AddAntiforgery(options =>
        {
            options.Cookie.Name = "ambrosio.auth.af";
            options.Cookie.Path = OpenIddictSetup.AuthPathBase;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.FormFieldName = AntiforgeryFieldName;
        });

        services.AddAuthorization();

        services.AddAmbrosioMcpRateLimiter();

        // Quem chama a tool (preenchido pelo ScopeChallengeFilter) e o padrão `confirmar` (T022/T026).
        services.AddScoped<McpUserContext>();
        services.AddScoped<WriteToolHelper>();

        // Download do arquivo da conversa para enviar_imagem (T066): HttpClient próprio, com as travas.
        services.AddSingleton<Ambrosio.McpServer.Mcp.Files.SafeFileDownloader>();

        services.AddMcpServer()
            .WithHttpTransport(transport => transport.SessionMode = HttpServerSessionMode.Stateless)
            // Copia das opções do SDK: elas trazem o TypeInfoResolver; um JsonSerializerOptions vazio
            // derruba o host no arranque assim que existe uma tool ("must specify a TypeInfoResolver").
            .WithToolsFromAssembly(typeof(McpSetup).Assembly, new JsonSerializerOptions(ModelContextProtocol.McpJsonUtilities.DefaultOptions)
            {
                // Mesmo contrato JSON da API REST: camelCase e enums como string minúscula, para que
                // a vendedora veja os mesmos valores no Swagger e no ChatGPT (FR-010).
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true
            })
            .WithRequestFilters(filters =>
            {
                filters.AddCallToolFilter(ScopeChallengeFilter.Create());
                filters.AddListToolsFilter(ToolDescriptorFilter.Create());
            })
            .WithMessageFilters(filters => filters.AddOutgoingFilter(ToolDescriptorFilter.CreatePromotion()));

        return services;
    }

    /// <summary>
    /// Autenticação mista do <c>/mcp</c> (R7): sem token passa (listagem e tool <c>noauth</c>; as
    /// protegidas devolvem o desafio no resultado); token presente e inválido — assinatura, <c>iss</c>,
    /// <c>aud</c>, validade, ou o HS256 das telas — responde HTTP 401 aqui, com o
    /// <c>resource_metadata</c> que leva o cliente ao authorization server.
    /// </summary>
    public static IApplicationBuilder UseAmbrosioMcpTokenGuard(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments(McpPath)
                && context.Request.Headers.Authorization.Count > 0
                && context.User.Identity?.IsAuthenticated != true)
            {
                var resourceUrl = context.RequestServices
                    .GetRequiredService<Microsoft.Extensions.Options.IOptions<McpSettings>>().Value.ResourceUrl;

                // Motivo da recusa (audience, issuer, assinatura, validade) no log, sem o token: é a
                // primeira coisa a olhar quando o ChatGPT diz que as ferramentas não estão disponíveis.
                var failure = (await context.AuthenticateAsync(Scheme)).Failure;
                context.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("Ambrosio.McpServer.TokenGuard")
                    .LogWarning(
                        "MCP: token recusado com 401 ({Reason}).",
                        failure?.Message ?? "esquema Authorization não reconhecido ou token ausente");

                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                // Cabeçalho HTTP só aceita ASCII (o Kestrel derruba a resposta com 500 se houver
                // acento): a descrição do 401 fica em inglês, sem diacríticos.
                context.Response.Headers.WWWAuthenticate = McpChallenge.Build(
                    resourceUrl, McpChallenge.InvalidToken, "The access token is invalid or expired");

                return;
            }

            await next(context);
        });
}
