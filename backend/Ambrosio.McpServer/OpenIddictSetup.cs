using Ambrosio.DTO.Settings;
using Ambrosio.Infra.Context;
using Ambrosio.Infra.Context.OAuth;
using Ambrosio.McpServer.Auth;
using Ambrosio.McpServer.Auth.Cimd;
using Ambrosio.McpServer.Auth.Handlers;
using Ambrosio.McpServer.Auth.Stores;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Ambrosio.McpServer;

/// <summary>
/// Configuração do authorization server OAuth 2.1 (feature 007, R3/R5/R13/R14).
/// </summary>
/// <remarks>
/// O que o ChatGPT exige e o OpenIddict não traz pronto — CIMD, DCR, <c>resource</c> (RFC 8707) e
/// <c>iss</c> na resposta (RFC 9207) — vira handler em tasks próprios (R4/R5). Aqui fica o núcleo do
/// protocolo: issuer explícito, os endpoints sob <c>/auth</c>, PKCE S256 obrigatório, access token
/// JWT não criptografado e a chave RSA do <see cref="SigningKeyProvider"/>.
/// </remarks>
public static class OpenIddictSetup
{
    /// <summary>Caminho do AS atrás do gateway; também o PathBase do host (R8/T015).</summary>
    public const string AuthPathBase = "/auth";

    /// <summary>
    /// Registra o OpenIddict sobre o <see cref="AmbrosioContext"/> existente, com as entidades de
    /// chave <see cref="long"/> mapeadas em <c>OAuthSetup</c> (R15).
    /// </summary>
    public static IServiceCollection AddAmbrosioAuthorizationServer(
        this IServiceCollection services,
        IConfiguration configuration,
        SigningKeyProvider signingKey)
    {
        var authServer = configuration.GetSection("Auth").Get<AuthServerSettings>() ?? new AuthServerSettings();
        var mcp = configuration.GetSection("Mcp").Get<McpSettings>() ?? new McpSettings();

        services.Configure<AuthServerSettings>(configuration.GetSection("Auth"));

        // CIMD e DCR (T037/T038): registro de clientes e download do documento com travas de SSRF.
        services.AddMemoryCache();
        services.AddSingleton<CimdDocumentFetcher>();
        services.AddScoped<ClientRegistrar>();

        // Instância já resolvida pelo Program: o host precisa do `kid` e da origem em log antes de
        // registrar serviço algum, e um container construído aqui produziria duas chaves locais
        // diferentes em Development.
        services.AddSingleton(signingKey);

        services.AddOpenIddict()

            // O store EF fica no AddCore(), não no AddServer() — UseEntityFrameworkCore() é
            // extensão de OpenIddictCoreBuilder.
            .AddCore(core =>
            {
                core.UseEntityFrameworkCore()
                    .UseDbContext<AmbrosioContext>()
                    .ReplaceDefaultEntities<OAuthApplication, OAuthAuthorization, OAuthScope, OAuthToken, long>()
                    // O asset net8.0 do OpenIddict 7.7.2 foi compilado contra o EF Core 8, e as operações
                    // em lote dele (ExecuteUpdate/ExecuteDelete) não existem no assembly Relational do
                    // EF Core 9 do projeto: MissingMethodException na revogação da família de um refresh
                    // reutilizado e no revoke (research R19). Sem lote, o store carrega e atualiza
                    // linha a linha — volume pequeno no uso próprio.
                    .DisableBulkOperations();

                // Revogações em lote reescritas para o EF Core 9 (o DisableBulkOperations não as cobre).
                core.ReplaceTokenStore<OAuthToken, AmbrosioTokenStore>(ServiceLifetime.Scoped);
                core.ReplaceAuthorizationStore<OAuthAuthorization, AmbrosioAuthorizationStore>(ServiceLifetime.Scoped);
            })

            .AddServer(server =>
            {
                // Issuer explícito: deduzido da requisição ele sairia com o host errado (ou http)
                // atrás do gateway, e o `iss` do token não bateria com o ValidIssuer do MCP (T018).
                server.SetIssuer(new Uri(authServer.Issuer, UriKind.Absolute));

                // Endpoints do contrato (contracts/oauth-endpoints.md §1), todos relativos ao
                // PathBase /auth.
                server.SetAuthorizationEndpointUris("connect/authorize")
                    .SetTokenEndpointUris("connect/token")
                    .SetUserInfoEndpointUris("connect/userinfo")
                    .SetRevocationEndpointUris("connect/revoke")
                    .SetJsonWebKeySetEndpointUris(".well-known/jwks")
                    .SetConfigurationEndpointUris(Array.Empty<string>());

                // Sem SetConfigurationEndpointUris: o documento do OpenIddict não traz DCR nem CIMD, e
                // o contrato exige o MESMO documento em /auth/.well-known/openid-configuration e em
                // /.well-known/oauth-authorization-server/auth — os dois saem do MetadataEndpoints.

                server.AllowAuthorizationCodeFlow()
                    .AllowRefreshTokenFlow();

                // PKCE obrigatório, só S256 (requisito do ChatGPT; `plain` precisa virar
                // invalid_request — T030).
                server.RequireProofKeyForCodeExchange();
                server.Configure(options =>
                {
                    options.CodeChallengeMethods.Clear();
                    options.CodeChallengeMethods.Add(CodeChallengeMethods.Sha256);
                });

                // RFC 8707 (R5, T035): o único recurso é o MCP. Recurso diferente (inclusive com barra
                // no final) vira invalid_target no próprio OpenIddict; a obrigatoriedade do parâmetro
                // é o ResourceIndicatorHandler. Os clientes do ChatGPT são criados em tempo de
                // execução (DCR/CIMD) sem permissões por escopo ou recurso: o que limita escopo é a
                // semente de oauth_scopes, e o que limita recurso é a lista registrada aqui.
                server.RegisterResources(mcp.ResourceUrl);
                server.IgnoreResourcePermissions();
                server.IgnoreScopePermissions();

                // `iss` em toda resposta de autorização (RFC 9207, T036) é nativo do OpenIddict 7
                // (handler Authentication.AttachIssuer) e sai igual ao issuer configurado acima.

                server.AddEventHandler(CimdClientHandler.Descriptor);
                server.AddEventHandler(ResourceIndicatorHandler.AuthorizationDescriptor);
                server.AddEventHandler(ResourceIndicatorHandler.TokenDescriptor);
                server.AddEventHandler(RevokeAuthorizationHandler.Descriptor);

                // Recusas do OAuth em Warning (produção não registra Information): sem isto uma
                // conexão do ChatGPT que falha no authorize ou no token não deixa rastro.
                foreach (var descriptor in OAuthRejectionLogger.Descriptors)
                {
                    server.AddEventHandler(descriptor);
                }

                // JWT assinado, não criptografado: o MCP valida com a chave pública do JWKS, então
                // um JWE quebraria a validação cross-host (R13).
                server.DisableAccessTokenEncryption();

                // Validades da spec (R14): acesso 30 min, refresh 14 dias. A rotação com
                // deslizamento e a revogação da família no reuso são o comportamento padrão do
                // OpenIddict para refresh tokens, não uma opção ligada aqui.
                server.SetAccessTokenLifetime(TimeSpan.FromMinutes(authServer.AccessTokenMinutes));
                server.SetRefreshTokenLifetime(TimeSpan.FromDays(authServer.RefreshTokenDays));

                // Sem tolerância de reuso: o padrão do OpenIddict aceita o mesmo refresh por alguns
                // segundos (para clientes com chamadas concorrentes), o que deixaria um refresh vazado
                // ser trocado sem derrubar a família (R14, T031).
                server.SetRefreshTokenReuseLeeway(TimeSpan.Zero);

                // Passthrough: authorize, token e userinfo caem em controllers/pages deste host,
                // porque é aí que entram o cookie de sessão do AS, o consentimento e o `sub`.
                var aspNetCore = server.UseAspNetCore()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableTokenEndpointPassthrough()
                    .EnableUserInfoEndpointPassthrough();

                // Development e homologação rodam em http (localhost); o startup só aceita issuer http
                // em Development (Program.ValidateSettings), e em produção ele é https.
                if (authServer.Issuer.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                {
                    aspNetCore.DisableTransportSecurityRequirement();
                }

                // Assinador único: RSA 2048 (R13). A chave é a do SigningKeyProvider resolvido pelo
                // Program — obrigatoriamente de Auth__SigningKeyPem em Homolog e Production.
                server.AddSigningKey(signingKey.SecurityKey);

                // OpenIddict EXIGE uma chave de criptografia além da de assinatura, e reclama no
                // primeiro request se ela faltar — inclusive com DisableAccessTokenEncryption()
                // ligado, porque a chave também protege os bilhetes internos de autenticação.
                // AddEphemeralEncryptionKey() resolvia o erro e era a armadilha: trocaria a chave a
                // cada restart, exatamente o que R13 proíbe.
                //
                // Reaproveitamos a MESMA RSA da assinatura. É um concessionamento deliberado (uma
                // chave em dois papéis), pago de propósito para não exigir um segundo segredo no
                // ambiente nem um segundo arquivo em %APPDATA%: o requisito real da feature é que a
                // chave sobreviva ao restart, e dividir assinatura de criptografia só seria
                // obrigatório se os dois serviços corressem em hosts separados — o que R8 descartou.
                server.AddEncryptionKey(signingKey.SecurityKey);
            });

        // Sem AddValidation() aqui: proteger `/auth/connect/userinfo` com o pipeline de validação do
        // OpenIddict exigiria o pacote OpenIddict.Validation, e o mesmo access token já é validado
        // pelo JwtBearer contra o JWKS local (T018) — um esquema só, a mesma chave pública para o MCP
        // e para o userinfo, e nada de segunda pilha de validação para manter alinhada.

        return services;
    }
}
