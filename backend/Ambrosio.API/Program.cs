using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ambrosio.API.Swagger;
using Ambrosio.Application;
using Ambrosio.Application.Settings;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Configuração (research §16): appsettings*.json + variáveis de ambiente (__).
// ---------------------------------------------------------------------------
var settings = new AmbrosioSettings
{
    ConnectionString = builder.Configuration.GetConnectionString("AmbrosioContext") ?? string.Empty,
    Auth = builder.Configuration.GetSection("Auth").Get<Ambrosio.DTO.Settings.AuthSettings>()
            ?? new Ambrosio.DTO.Settings.AuthSettings(),
    CpfProtection = builder.Configuration.GetSection("CpfProtection")
            .Get<Ambrosio.DTO.Settings.CpfProtectionSettings>()
            ?? new Ambrosio.DTO.Settings.CpfProtectionSettings(),
    ObjectStorage = builder.Configuration.GetSection("ObjectStorage")
            .Get<Ambrosio.DTO.Settings.ObjectStorageSettings>()
            ?? new Ambrosio.DTO.Settings.ObjectStorageSettings(),
    MaisVendas = builder.Configuration.GetSection("MaisVendas")
            .Get<Ambrosio.DTO.Settings.MaisVendasSettings>()
            ?? new Ambrosio.DTO.Settings.MaisVendasSettings()
};

// Falha no startup em vez de rodar com chave vazia: CPFs cifrados com chave errada são
// irrecuperáveis e tokens assinados por segredo fraco não dão autenticidade (research §16).
// No design time do EF (ex.: `dotnet ef migrations bundle` no docker build) o host só é montado para
// obter o DbContext, sem os segredos de runtime — a validação vale para a API rodando.
if (!Microsoft.EntityFrameworkCore.EF.IsDesignTime)
{
    ValidateSettings(settings);
}

// ---------------------------------------------------------------------------
// DI do backend (composition root na camada Application — skill dotnet-architecture).
// ---------------------------------------------------------------------------
builder.Services.ConfigureServices(opt =>
{
    opt.ConnectionString = settings.ConnectionString;
    opt.Auth.JwtSecret = settings.Auth.JwtSecret;
    opt.Auth.Issuer = settings.Auth.Issuer;
    opt.Auth.Audience = settings.Auth.Audience;
    opt.Auth.ExpirationMinutes = settings.Auth.ExpirationMinutes;
    opt.Auth.GoogleClientId = settings.Auth.GoogleClientId;
    opt.CpfProtection.Pepper = settings.CpfProtection.Pepper;
    opt.CpfProtection.EncryptionKey = settings.CpfProtection.EncryptionKey;

    // Storage de imagens (feature 004): a seção inteira vai adiante como IOptions; o validation
    // abaixo já recusou startup com configuração incompleta (FR-020).
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

    // Importação do +Vendas (feature 006, research §13; feature 008, research R2/R6): a seção inteira vai
    // adiante como IOptions; o ValidateSettings abaixo já recusou startup com configuração fora das
    // faixas ou sem a chave de cifra das credenciais.
    opt.MaisVendas.Collector = settings.MaisVendas.Collector;
    opt.MaisVendas.BaseUrl = settings.MaisVendas.BaseUrl;
    opt.MaisVendas.BlockDays = settings.MaisVendas.BlockDays;
    opt.MaisVendas.MaxRangeMonths = settings.MaisVendas.MaxRangeMonths;
    opt.MaisVendas.CredentialsKey = settings.MaisVendas.CredentialsKey;
    opt.MaisVendas.MaxConcurrentRuns = settings.MaisVendas.MaxConcurrentRuns;
    opt.MaisVendas.MaxFailedLogins = settings.MaisVendas.MaxFailedLogins;
    opt.MaisVendas.LockoutMinutes = settings.MaisVendas.LockoutMinutes;
    opt.MaisVendas.MinDelayMs = settings.MaisVendas.MinDelayMs;
    opt.MaisVendas.MaxDelayMs = settings.MaisVendas.MaxDelayMs;
    opt.MaisVendas.Fake.DelayMs = settings.MaisVendas.Fake.DelayMs;
});

// ---------------------------------------------------------------------------
// MVC: enums como string minúscula no JSON (research §7) e erros de binding no formato
// ErrorResult (T024) em vez do ProblemDetails automático.
// ---------------------------------------------------------------------------
builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)))
    .ConfigureApiBehaviorOptions(options => options.SuppressModelStateInvalidFilter = true);

// ---------------------------------------------------------------------------
// Autenticação/autorização: JWT Bearer HS256 emitido pelo Ambrosio (constituição §VI,
// research §3). MapInboundClaims = false mantém o claim "sub" sem renomear.
// ---------------------------------------------------------------------------
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = settings.Auth.Issuer,
            ValidAudience = settings.Auth.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.Auth.JwtSecret)),
            ClockSkew = TimeSpan.FromMinutes(1)
        };
        options.TokenValidationParameters.NameClaimType = "name";
    });

builder.Services.AddAuthorization();

// ---------------------------------------------------------------------------
// Swagger com botão Authorize (FR-025, SC-001).
// ---------------------------------------------------------------------------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Ambrosio API",
        Version = "1.0.0",
        Description = "API de cadastro e consulta de clientes e pedidos (feature 001). " +
                      "Todas as rotas exceto `/auth/*` exigem `Authorization: Bearer <token>`. " +
                      "Cada usuário enxerga apenas os próprios clientes e pedidos; recursos de " +
                      "outro usuário respondem 404. " +
                      "Rotas de imagem (feature 004): `POST /images/uploads` declara um envio e devolve " +
                      "a URL pré-assinada de `PUT`, `POST /images/uploads/{imageId}/confirm` confere o " +
                      "binário que chegou ao storage, `POST /images` grava pelo servidor (multipart, " +
                      "sem EXIF), `GET /images/{imageId}` consulta e `DELETE /images/{imageId}` exclui. " +
                      "As URLs de leitura são temporárias e o bucket é privado. " +
                      "Importação do +Vendas (feature 006): `GET /imports/mais-vendas/defaults` traz as " +
                      "datas sugeridas, `POST /imports/mais-vendas/runs` inicia uma execução (202; login " +
                      "e senha do +Vendas valem só para ela e não são guardados em lugar nenhum), " +
                      "`GET /imports/mais-vendas/runs/current` acompanha o progresso, " +
                      "`GET /imports/mais-vendas/runs/{runId}` é o relatório, " +
                      "`GET /imports/mais-vendas/runs` é o histórico paginado e " +
                      "`POST /imports/mais-vendas/runs/{runId}/cancel` pede o cancelamento. Nenhuma " +
                      "resposta de importação contém credencial, e o período é reimportável sem " +
                      "duplicar nada."
    });

    // T094: o wrapper paginado saía como "CustomerInfoPagedResult"; o contrato
    // (contracts/openapi.yaml) define PagedCustomerInfo/PagedOrderInfo.
    options.CustomSchemaIds(OpenApiNaming.SchemaId);

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Cole o token do Ambrosio obtido em POST /auth/login, POST /auth/register " +
                      "ou POST /auth/google. Não inclua o prefixo \"Bearer\"."
    });

    // O requisito de segurança é declarado POR OPERAÇÃO no ContractAlignmentOperationFilter, e não
    // globalmente (T094): o contrato marca as rotas de /auth/* com `security: []`, e a
    // Microsoft.OpenApi não serializa uma lista de requisitos vazia — com um requisito global, uma
    // operação não tem como "desligar" a autenticação no documento, e o Swagger mostraria o cadeado
    // na rota que serve justamente para obter o token. Sem requisito global, cada operação protegida
    // declara o Bearer e as [AllowAnonymous] ficam sem `security` nenhum, o mesmo efeito do contrato.
    var xmlPath = Path.Combine(AppContext.BaseDirectory, $"{typeof(Program).Assembly.GetName().Name}.xml");
    if (File.Exists(xmlPath))
    {
        options.IncludeXmlComments(xmlPath);
    }

    // T094: parâmetros de query em camelCase e o requisito Bearer por operação.
    options.OperationFilter<ContractAlignmentOperationFilter>();
});

// ---------------------------------------------------------------------------
// CORS: AllowAnyOrigin SOMENTE em Development (constituição §VI).
// ---------------------------------------------------------------------------
const string CorsPolicyName = "ambrosio-dev";

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddCors(options => options.AddPolicy(
        CorsPolicyName,
        policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));
}

// ---------------------------------------------------------------------------
// Reverse proxy (Homolog/Production): o gateway nginx publica a API em /api, remove o prefixo e
// informa-o em X-Forwarded-Prefix (deploy/nginx/routes.inc). Com isso Request.PathBase vira
// "/api" e o esquema/IP reais chegam por X-Forwarded-Proto/For. A API só é alcançável pela rede
// interna do compose, então qualquer proxy é aceito.
// ---------------------------------------------------------------------------
var behindReverseProxy = builder.Configuration.GetValue<bool>("ReverseProxy:Enabled");

if (behindReverseProxy)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
                                   | ForwardedHeaders.XForwardedProto
                                   | ForwardedHeaders.XForwardedPrefix;
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();
    });
}

var app = builder.Build();

if (behindReverseProxy)
{
    app.UseForwardedHeaders();
}

// Swagger UI em todos os ambientes: a documentação não contém dados sensíveis (research §14).
// Atrás do gateway, o documento declara o prefixo (/api) em `servers`, para o "Try it out"
// chamar /api/auth/google e não /auth/google.
app.UseSwagger(options => options.PreSerializeFilters.Add((document, request) =>
{
    if (request.PathBase.HasValue)
    {
        document.Servers = [new OpenApiServer { Url = request.PathBase.Value }];
    }
}));
app.UseSwaggerUI();

if (app.Environment.IsDevelopment())
{
    app.UseCors(CorsPolicyName);
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

// Recusa startup com configuração incompleta (research §16): connection string vazia,
// segredo JWT curto, pepper vazio ou chave AES que não é base64 de exatamente 32 bytes.
// (Comentário normal: funções locais em arquivos de statement-top-level não recebem XML doc.)
static void ValidateSettings(AmbrosioSettings settings)
{
    var errors = new List<string>();

    if (string.IsNullOrWhiteSpace(settings.ConnectionString))
    {
        errors.Add("ConnectionStrings__AmbrosioContext está vazia.");
    }

    if (Encoding.UTF8.GetByteCount(settings.Auth.JwtSecret) < 32)
    {
        errors.Add("Auth__JwtSecret precisa ter pelo menos 32 bytes UTF-8.");
    }

    if (string.IsNullOrWhiteSpace(settings.CpfProtection.Pepper))
    {
        errors.Add("CpfProtection__Pepper está vazio.");
    }

    byte[]? key = null;
    try
    {
        key = Convert.FromBase64String(settings.CpfProtection.EncryptionKey);
    }
    catch (FormatException)
    {
        // Tratado abaixo como erro de configuração.
    }

    if (key is null || key.Length != 32)
    {
        errors.Add("CpfProtection__EncryptionKey precisa ser base64 de exatamente 32 bytes.");
    }

    ValidateObjectStorage(settings.ObjectStorage, errors);
    ValidateMaisVendas(settings.MaisVendas, errors);

    if (errors.Count > 0)
    {
        throw new InvalidOperationException(
            "Configuração inválida da API Ambrosio: " + string.Join(" ", errors) +
            " Defina as variáveis de ambiente ou o appsettings antes de iniciar.");
    }
}

// Storage de imagens (feature 004, FR-020/FR-021, research §12): a API não sobe com configuração
// incompleta ou fora das faixas. Cada mensagem nomeia a VARIÁVEL e nunca mostra o valor — os valores
// podem ser segredo (AccessKey/SecretKey) e o texto do InvalidOperationException vai para o log e para
// o stderr do container.
static void ValidateObjectStorage(Ambrosio.DTO.Settings.ObjectStorageSettings storage, List<string> errors)
{
    if (!IsHttpUri(storage.ServiceUrl))
    {
        errors.Add("ObjectStorage__ServiceUrl deve ser uma URL absoluta http ou https.");
    }

    // PublicServiceUrl é opcional: quando informada, tem de ser URL válida (research §3).
    if (!string.IsNullOrWhiteSpace(storage.PublicServiceUrl) && !IsHttpUri(storage.PublicServiceUrl))
    {
        errors.Add("ObjectStorage__PublicServiceUrl deve ser uma URL absoluta http ou https.");
    }

    if (string.IsNullOrWhiteSpace(storage.Region))
    {
        errors.Add("ObjectStorage__Region está vazia.");
    }

    if (string.IsNullOrWhiteSpace(storage.Bucket))
    {
        errors.Add("ObjectStorage__Bucket está vazio.");
    }

    if (string.IsNullOrWhiteSpace(storage.AccessKey))
    {
        errors.Add("ObjectStorage__AccessKey está vazio.");
    }

    if (string.IsNullOrWhiteSpace(storage.SecretKey))
    {
        errors.Add("ObjectStorage__SecretKey está vazio.");
    }

    AddIfOutOfRange(errors, "ObjectStorage__PresignedReadMinutes", storage.PresignedReadMinutes, 1, 60);
    AddIfOutOfRange(errors, "ObjectStorage__PresignedUploadMinutes", storage.PresignedUploadMinutes, 1, 15);
    AddIfOutOfRange(errors, "ObjectStorage__PendingTtlMinutes", storage.PendingTtlMinutes, 1, 1440);
    AddIfOutOfRange(errors, "ObjectStorage__CleanupIntervalMinutes", storage.CleanupIntervalMinutes, 1, 60);

    if (storage.MaxUploadBytes <= 0)
    {
        errors.Add("ObjectStorage__MaxUploadBytes deve ser maior que zero.");
    }

    // Só se aceita tipo que tem extensão mapeada na chave do objeto (StoredImage.BuildStorageKey).
    string[] supportedMimeTypes = ["image/jpeg", "image/png", "image/webp"];

    if (storage.AllowedMimeTypes.Count == 0)
    {
        errors.Add("ObjectStorage__AllowedMimeTypes está vazio.");
    }
    else
    {
        foreach (var mimeType in storage.AllowedMimeTypes)
        {
            if (!supportedMimeTypes.Contains(mimeType, StringComparer.OrdinalIgnoreCase))
            {
                errors.Add(
                    $"ObjectStorage__AllowedMimeTypes aceita apenas {string.Join(", ", supportedMimeTypes)}.");
                break;
            }
        }
    }

    static bool IsHttpUri(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}

// Importação do +Vendas (feature 006, research §13): a API não sobe com a seção fora das faixas. Cada
// mensagem nomeia a VARIÁVEL e o intervalo aceito, e nunca mostra o valor — o texto do
// InvalidOperationException vai para o log e para o stderr do container.
static void ValidateMaisVendas(Ambrosio.DTO.Settings.MaisVendasSettings maisVendas, List<string> errors)
{
    const string collector = "MaisVendas__Collector";

    if (!maisVendas.IsPlaywright && !maisVendas.IsFake)
    {
        errors.Add(
            $"{collector} aceita apenas " +
            $"{Ambrosio.DTO.Settings.MaisVendasSettings.COLLECTOR_PLAYWRIGHT} ou " +
            $"{Ambrosio.DTO.Settings.MaisVendasSettings.COLLECTOR_FAKE}.");
    }

    // Fake é a origem de teste controlada da homologação (research §3). Em produção um fake
    // "importando com sucesso" escreveria pedidos inventados na base da vendedora com o carimbo de uma
    // execução concluída — o pior resultado possível para a feature. A recusa é de startup, não de
    // runtime: o deploy falha cedo e visível.
    if (maisVendas.IsFake
        && string.Equals(
            Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"),
            "Production",
            StringComparison.OrdinalIgnoreCase))
    {
        errors.Add($"{collector} não aceita Fake em Production.");
    }

    if (!Uri.TryCreate(maisVendas.BaseUrl, UriKind.Absolute, out var baseUri)
        || baseUri.Scheme != Uri.UriSchemeHttps)
    {
        errors.Add("MaisVendas__BaseUrl deve ser uma URL absoluta https.");
    }

    AddIfOutOfRange(errors, "MaisVendas__BlockDays", maisVendas.BlockDays, 1, 31);
    AddIfOutOfRange(errors, "MaisVendas__MaxRangeMonths", maisVendas.MaxRangeMonths, 1, 12);
    AddIfOutOfRange(errors, "MaisVendas__MaxConcurrentRuns", maisVendas.MaxConcurrentRuns, 1, 8);
    AddIfOutOfRange(errors, "MaisVendas__MaxFailedLogins", maisVendas.MaxFailedLogins, 1, 10);
    AddIfOutOfRange(errors, "MaisVendas__LockoutMinutes", maisVendas.LockoutMinutes, 1, 1440);
    AddIfOutOfRange(errors, "MaisVendas__MinDelayMs", maisVendas.MinDelayMs, 100, 5000);
    AddIfOutOfRange(errors, "MaisVendas__MaxDelayMs", maisVendas.MaxDelayMs, 100, 5000);
    AddIfOutOfRange(errors, "MaisVendas__Fake__DelayMs", maisVendas.Fake.DelayMs, 0, 5000);

    // Dois valores dentro da faixa, mas invertidos, não são detectáveis por faixa sozinha: o jitter do
    // ritmo (FR-043) viraria espera negativa.
    if (maisVendas.MinDelayMs > maisVendas.MaxDelayMs)
    {
        errors.Add("MaisVendas__MinDelayMs não pode ser maior que MaisVendas__MaxDelayMs.");
    }

    // Cifra das credenciais do +Vendas (feature 008, research R2; constituição §VI emenda 3.4.1): sem a
    // chave dedicada a API não sobe — guardaria senha de terceiro com o primeiro valor que aparecesse,
    // e uma chave de tamanho errado abriria a cifra em execução, no meio de uma importação.
    if (TryBase64Length(maisVendas.CredentialsKey) != 32)
    {
        errors.Add("MaisVendas__CredentialsKey deve ser base64 de exatamente 32 bytes.");
    }

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
}

static void AddIfOutOfRange(List<string> errors, string name, int value, int min, int max)
{
    if (value < min || value > max)
    {
        errors.Add($"{name} deve estar entre {min} e {max} (valor atual não é mostrado).");
    }
}
