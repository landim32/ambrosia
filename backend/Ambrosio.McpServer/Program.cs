using System.Text;
using Ambrosio.Application;
using Ambrosio.Application.HostedServices;
using Ambrosio.Application.Settings;
using Ambrosio.DTO.Settings;
using Ambrosio.McpServer;
using Ambrosio.McpServer.Auth;
using Ambrosio.McpServer.Auth.Endpoints;
using Ambrosio.McpServer.Middlewares;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Configuração (feature 007, T010). Montada seção por seção como na API
// (`GetSection<T>().Get<T>() ?? new T()`), nunca IOptions no host: os endereços canônicos vêm
// sempre de `Auth:Issuer` e `Mcp:ResourceUrl` e jamais de valor fixo no código (R5/R8).
// ---------------------------------------------------------------------------

var authServer = builder.Configuration.GetSection("Auth").Get<AuthServerSettings>() ?? new AuthServerSettings();
var mcp = builder.Configuration.GetSection("Mcp").Get<McpSettings>() ?? new McpSettings();

var settings = new AmbrosioSettings
{
    ConnectionString = builder.Configuration.GetConnectionString("AmbrosioContext") ?? string.Empty,
    Auth = new AuthSettings
    {
        // Só o que este host realmente usa. `Issuer`/`Audience` do AuthSettings ficam de propósito
        // em branco: em `Auth:Issuer` mora o issuer do authorization server (uma URL), um conceito
        // diferente do nome curto do JWT HS256 das telas — misturá-los produziria tokens com `iss`
        // inesperado. Ver a nota em AuthServerSettings.
        JwtSecret = authServer.JwtSecret,
        GoogleClientId = authServer.GoogleClientId,
    },
    CpfProtection = builder.Configuration.GetSection("CpfProtection").Get<CpfProtectionSettings>()
        ?? new CpfProtectionSettings(),
    ObjectStorage = builder.Configuration.GetSection("ObjectStorage").Get<ObjectStorageSettings>()
        ?? new ObjectStorageSettings(),
    MaisVendas = builder.Configuration.GetSection("MaisVendas").Get<MaisVendasSettings>()
        ?? new MaisVendasSettings(),
};

// O guard de design-time é o mesmo da API: `dotnet ef` carrega este Program.cs para descobrir o
// contexto e não tem os segredos do ambiente na mão.
if (!EF.IsDesignTime)
{
    ValidateSettings(settings, authServer, mcp, builder.Environment);
}

// A chave de assinatura é resolvida AQUI, antes de qualquer registro, porque o AddOpenIddict() a
// consome durante a montagem dos serviços. Deixá-la ser instanciada pelo container obrigaria um
// BuildServiceProvider() no meio do registro — dois containers, e em Development duas chaves locais
// diferentes. O log da origem acontece depois do Build, quando já existe logger.
var signingKey = new SigningKeyProvider(authServer, builder.Environment);

builder.Services.ConfigureServices(opt =>
{
    opt.ConnectionString = settings.ConnectionString;
    opt.Auth.JwtSecret = settings.Auth.JwtSecret;
    opt.Auth.GoogleClientId = settings.Auth.GoogleClientId;
    opt.CpfProtection.Pepper = settings.CpfProtection.Pepper;
    opt.CpfProtection.EncryptionKey = settings.CpfProtection.EncryptionKey;
    opt.ObjectStorage.ServiceUrl = settings.ObjectStorage.ServiceUrl;
    opt.ObjectStorage.PublicServiceUrl = settings.ObjectStorage.PublicServiceUrl;
    opt.ObjectStorage.Region = settings.ObjectStorage.Region;
    opt.ObjectStorage.Bucket = settings.ObjectStorage.Bucket;
    opt.ObjectStorage.ForcePathStyle = settings.ObjectStorage.ForcePathStyle;
    opt.ObjectStorage.AccessKey = settings.ObjectStorage.AccessKey;
    opt.ObjectStorage.SecretKey = settings.ObjectStorage.SecretKey;
    opt.ObjectStorage.PresignedReadMinutes = settings.ObjectStorage.PresignedReadMinutes;
    opt.ObjectStorage.PresignedUploadMinutes = settings.ObjectStorage.PresignedUploadMinutes;
    opt.ObjectStorage.MaxUploadBytes = settings.ObjectStorage.MaxUploadBytes;
    opt.ObjectStorage.AllowedMimeTypes = settings.ObjectStorage.AllowedMimeTypes;
    opt.ObjectStorage.PendingTtlMinutes = settings.ObjectStorage.PendingTtlMinutes;
    opt.ObjectStorage.CleanupIntervalMinutes = settings.ObjectStorage.CleanupIntervalMinutes;
    opt.MaisVendas.Collector = settings.MaisVendas.Collector;
    opt.MaisVendas.BaseUrl = settings.MaisVendas.BaseUrl;
    opt.MaisVendas.BlockDays = settings.MaisVendas.BlockDays;
    opt.MaisVendas.MaxRangeDays = settings.MaisVendas.MaxRangeDays;
    opt.MaisVendas.MaxConcurrentRuns = settings.MaisVendas.MaxConcurrentRuns;
    opt.MaisVendas.MaxFailedLogins = settings.MaisVendas.MaxFailedLogins;
    opt.MaisVendas.LockoutMinutes = settings.MaisVendas.LockoutMinutes;
    opt.MaisVendas.MinDelayMs = settings.MaisVendas.MinDelayMs;
    opt.MaisVendas.MaxDelayMs = settings.MaisVendas.MaxDelayMs;
    opt.MaisVendas.Fake.DelayMs = settings.MaisVendas.Fake.DelayMs;
});

// O `ConfigureServices` compartilhado da camada Application registra dois IHostedService que só
// podem rodar num processo (a API): o `ImportRunWorker`, que consome a fila de importações, e o
// `PendingImageCleanupService`, que apaga pendentes. Deixar os dois ativos aqui faria a homologação
// ter DOIS consumidores para a mesma fila de importação — o mesmo run coletado duas vezes — e duas
// varreduras de limpeza disputando os mesmos objetos. Este host só lê e cancela importações pelo
// `IImportRunService`, então remove os dois descritores antes de qualquer resolve.
foreach (var descriptor in builder.Services
             .Where(d => d.ServiceType == typeof(IHostedService))
             .Where(d => d.ImplementationType == typeof(ImportRunWorker)
                         || d.ImplementationType == typeof(PendingImageCleanupService))
             .ToList())
{
    builder.Services.Remove(descriptor);
}

builder.Services.AddAmbrosioAuthorizationServer(builder.Configuration, signingKey);

// Endpoint MCP: validação do token do AS + transporte sem estado (T018/T020).
builder.Services.AddAmbrosioMcp(builder.Configuration, signingKey);

// Telas de login e consentimento do AS (T041/T042), em Auth/Pages.
builder.Services.AddRazorPages(options => options.RootDirectory = "/Auth/Pages");

// Bind das settings do MCP no container: sem isto, quem injeta IOptions<McpSettings> recebe o
// objeto vazio e o ResourceUrl some — foi o que derrubou a semente de escopos no primeiro teste.
builder.Services.Configure<McpSettings>(builder.Configuration.GetSection("Mcp"));

// Serviços próprios deste host. Os dois IHostedService da camada Application foram removidos logo
// acima; estes são do authorization server e pertencem só a este processo.
builder.Services.AddHostedService<ScopeSeeder>();

// OAuthPruneService fica FORA do arranque até o T017 ser resolvido (bloqueio registrado em
// specs/007-chatgpt-mcp-plugin/research.md). Não é preguiça: PruneAsync do OpenIddict 7.7.2 lança
// MissingMethodException sob o EF Core 9.0.22 do projeto, em 100% dos ciclos, e rodar o serviço
// assim publicaria no log da homologação um aviso de "tenta de novo em 6 horas" para uma falha que
// nunca se resolve — o pior tipo de ruído, porque parece saúde. A classe fica no repo para o T017 só
// precisar do registro de volta.

var app = builder.Build();

// A chave já estava resolvida antes dos registros; aqui ela só vai ao log, com o logger do host
// montado — e registrando a origem real, não a pretendida.
_ = signingKey.SecurityKey;

if (signingKey.Source == SigningKeySource.DevelopmentLocal)
{
    app.Logger.LogWarning(
        "Authorization server usando chave RSA local de desenvolvimento em {Path} (kid {KeyId}). "
        + "Ela não é segredo de produção: apagar o arquivo invalida as conexões já emitidas.",
        signingKey.LocalKeyPath, signingKey.KeyId);
}
else
{
    app.Logger.LogInformation(
        "Authorization server com chave RSA da configuração (kid {KeyId}).", signingKey.KeyId);
}

// Log explícito do adiamento: sem esta linha, a ausência do prune seria invisível — exatamente o
// formato de problema que demorou a aparecer (o serviço falhando em silêncio atrás de um warning).
app.Logger.LogWarning(
    "Limpeza periódica do OpenIddict DESABILITADA (T017): oauth_tokens e oauth_authorizations "
    + "crescem sem limite até o bloqueio de versão ser resolvido.");

// O authorization server e o MCP compartilham este processo (R8), mas cada um tem seu caminho na
// borda pública: o gateway encaminha /auth/* e /mcp sem reescrever nada, então o PathBase é o que
// faz os endpoints do AS aparecerem em /auth/connect/authorize sem duplicar o prefixo em cada
// descrição (T015).
app.UsePathBase(OpenIddictSetup.AuthPathBase);

// Atrás do gateway, o scheme/host reais vêm dos headers (mesma regra da API): sem isto o OpenIddict
// montaria URLs de descoberta com http e com o nome interno do container.
if (builder.Configuration.GetValue<bool>("ReverseProxy:Enabled"))
{
    var forwarded = new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
            | ForwardedHeaders.XForwardedPrefix
    };

    // Mesma regra da API: o padrão só confia em proxy de loopback, e o nginx chega pela rede do
    // Docker. Sem limpar as listas o X-Forwarded-Proto é ignorado, a requisição parece http e o
    // OpenIddict (issuer https) recusa tudo com ID2083 "This server only accepts HTTPS requests" —
    // visto em produção. O container não publica porta: só o nginx alcança este host.
    forwarded.KnownNetworks.Clear();
    forwarded.KnownProxies.Clear();

    app.UseForwardedHeaders(forwarded);
}

// CSS, logo e mascote das telas do AS, servidos sob /auth pelo PathBase (T043).
app.UseStaticFiles();

app.UseRouting();

// Origem de navegador proibida no /mcp cai aqui, antes de qualquer chave ser conferida (T021, R17).
app.UseAmbrosioMcpOriginGuard();

// Também executa os endpoints do OpenIddict (authorize/token/revoke/jwks) e serve o Protected
// Resource Metadata do SDK.
app.UseAuthentication();

// Depois da autenticação: o balde do rate limit é o `sub` do token (R17).
app.UseRateLimiter();

// Token presente e inválido no /mcp → 401 com resource_metadata (R7, T018/T020).
app.UseAmbrosioMcpTokenGuard();

app.UseAuthorization();

// Descoberta anônima (RFC 8414): o ChatGPT consulta antes de ter qualquer credencial (T019).
app.MapAmbrosioMetadataEndpoints();

// Authorization server: DCR, authorize/token/userinfo em passthrough e as telas (T038–T044).
app.MapAmbrosioRegistrationEndpoint();
app.MapAmbrosioAuthorizationEndpoints();
app.MapRazorPages();

// O MCP fica no caminho raiz, fora do PathBase do AS, e sem estado (R6). Autenticação mista (R7):
// sem RequireAuthorization — listagem e `sobre_ambrosio` são anônimas e as tools protegidas devolvem
// o desafio pelo ScopeChallengeFilter.
app.MapMcp(McpSetup.McpPath).RequireRateLimiting(McpRateLimiter.PolicyName);

app.Run();

// ---------------------------------------------------------------------------
// Validação de startup (T010), no padrão do `ValidateSettings` da API: acumula erros, nomeia a
// VARIÁVEL de ambiente e nunca ecoa o valor (mensagem de erro de config pode parar em log).
// funções locais em arquivos de statement-top-level não recebem XML doc
// ---------------------------------------------------------------------------

static void ValidateSettings(
    AmbrosioSettings settings,
    AuthServerSettings authServer,
    McpSettings mcp,
    IHostEnvironment environment)
{
    var errors = new List<string>();

    if (string.IsNullOrWhiteSpace(settings.ConnectionString))
    {
        errors.Add("ConnectionStrings__AmbrosioContext está vazia.");
    }

    // Endereços canônicos: obrigatórios, absolutos, sem barra no final (com barra o `aud` e o
    // `resource` deixam de bater por comparação exata — R5) e https fora de Development (R13).
    ValidateCanonicalUrl(errors, "Auth__Issuer", authServer.Issuer, environment);
    ValidateCanonicalUrl(errors, "Mcp__ResourceUrl", mcp.ResourceUrl, environment);

    // A tela de login do AS reaproveita IAuthService, que devolve o LoginResult com o JWT HS256 das
    // telas dentro. Este host descarta esse token, mas o serviço o assina de qualquer forma: sem
    // chave de pelo menos 32 bytes o login do authorization server quebra em execução, então a
    // exigência é real e a recusa é aqui, no startup, não numa 500 da tela.
    if (string.IsNullOrWhiteSpace(authServer.JwtSecret)
        || Encoding.UTF8.GetByteCount(authServer.JwtSecret) < 32)
    {
        errors.Add("Auth__JwtSecret deve ter no mínimo 32 bytes (é o segredo do HMAC do token que "
                   + "IAuthService assina durante o login, mesmo sendo descartado pelo AS).");
    }

    // R13 + constituição §VI (emenda 3.4.0): sem a chave RSA o host recusa o startup em Homolog e
    // Production. Em Development é permitido vazio — SigningKeyProvider gera e reusa uma local.
    if (string.IsNullOrWhiteSpace(authServer.SigningKeyPem) && !environment.IsDevelopment())
    {
        errors.Add("Auth__SigningKeyPem está vazia; é obrigatória em Homolog e Production "
                   + "(chave RSA 2048 em PEM PKCS#8 que assina os tokens do authorization server).");
    }

    AddIfOutOfRange(errors, "Auth__AccessTokenMinutes", authServer.AccessTokenMinutes, 1, 1440);
    AddIfOutOfRange(errors, "Auth__RefreshTokenDays", authServer.RefreshTokenDays, 1, 365);

    if (authServer.AllowedRedirectUris.Count == 0)
    {
        errors.Add("Auth__AllowedRedirectUris está vazia; sem nenhuma redirect URI o ChatGPT não "
                   + "consegue registrar o cliente nem por DCR nem por CIMD.");
    }

    if (authServer.Cimd.AllowedHosts.Count == 0)
    {
        errors.Add("Auth__Cimd__AllowedHosts está vazia; o CIMD ficaria com lista aberta de hosts "
                   + "para baixar documento de cliente (vetor de SSRF — R4).");
    }

    if (string.IsNullOrWhiteSpace(settings.CpfProtection.Pepper))
    {
        errors.Add("CpfProtection__Pepper está vazio.");
    }

    if (string.IsNullOrWhiteSpace(settings.CpfProtection.EncryptionKey)
        || TryBase64Length(settings.CpfProtection.EncryptionKey) != 32)
    {
        errors.Add("CpfProtection__EncryptionKey deve ser base64 de exatamente 32 bytes.");
    }

    // As tools de imagem gravam no mesmo storage da API; faltar endpoint/par de chaves/bucket se
    // traduz em falha de tool no meio de uma conversa, então o host também recusa aqui. Os tetos de
    // prazo (PresignedReadMinutes etc.) continuam validados só pela API.
    if (!IsHttpUri(settings.ObjectStorage.ServiceUrl))
    {
        errors.Add("ObjectStorage__ServiceUrl deve ser uma URL absoluta http(s).");
    }

    if (string.IsNullOrWhiteSpace(settings.ObjectStorage.Region))
    {
        errors.Add("ObjectStorage__Region está vazia.");
    }

    if (string.IsNullOrWhiteSpace(settings.ObjectStorage.Bucket))
    {
        errors.Add("ObjectStorage__Bucket está vazio.");
    }

    if (string.IsNullOrWhiteSpace(settings.ObjectStorage.AccessKey)
        || string.IsNullOrWhiteSpace(settings.ObjectStorage.SecretKey))
    {
        errors.Add("ObjectStorage__AccessKey e ObjectStorage__SecretKey são obrigatórios.");
    }

    if (settings.ObjectStorage.MaxUploadBytes <= 0)
    {
        errors.Add("ObjectStorage__MaxUploadBytes deve ser maior que zero.");
    }

    if (settings.ObjectStorage.AllowedMimeTypes.Count == 0)
    {
        errors.Add("ObjectStorage__AllowedMimeTypes está vazio.");
    }

    if (errors.Count > 0)
    {
        throw new InvalidOperationException(
            "Configuração inválida do Ambrosio.McpServer: " + string.Join(" ", errors) +
            " Defina as variáveis de ambiente ou o appsettings antes de iniciar.");
    }
}

// função local: comparação dos dois endereços canônicos — absoluta, sem barra final, https fora de
// Development.
static void ValidateCanonicalUrl(List<string> errors, string name, string value, IHostEnvironment environment)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        errors.Add($"{name} está vazio.");
        return;
    }

    if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
        || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
    {
        errors.Add($"{name} deve ser uma URL absoluta http(s) (o valor atual não é).");
        return;
    }

    if (value.EndsWith("/", StringComparison.Ordinal))
    {
        errors.Add($"{name} não pode terminar em barra.");
    }

    // http só em Development ou em loopback: a homologação roda no gateway local
    // (http://localhost:8080), e nenhum endereço público pode sair sem TLS.
    if (!environment.IsDevelopment() && uri.Scheme != Uri.UriSchemeHttps && !uri.IsLoopback)
    {
        errors.Add($"{name} deve usar https fora de Development (http só é aceito em localhost).");
    }
}

static void AddIfOutOfRange(List<string> errors, string name, int value, int min, int max)
{
    if (value < min || value > max)
    {
        errors.Add($"{name} deve estar entre {min} e {max} (valor atual não é mostrado).");
    }
}

static bool IsHttpUri(string? value) =>
    !string.IsNullOrWhiteSpace(value)
    && Uri.TryCreate(value, UriKind.Absolute, out var uri)
    && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

// função local: devolve o tamanho em bytes do base64, ou -1 se não for base64 válido — sem lançar.
static int TryBase64Length(string? value)
{
    try
    {
        return Convert.FromBase64String(value ?? string.Empty).Length;
    }
    catch (FormatException)
    {
        return -1;
    }
}
