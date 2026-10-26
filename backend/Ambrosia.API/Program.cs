using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ambrosia.API.Swagger;
using Ambrosia.Application;
using Ambrosia.Application.Settings;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Configuração (research §16): appsettings*.json + variáveis de ambiente (__).
// ---------------------------------------------------------------------------
var settings = new AmbrosiaSettings
{
    ConnectionString = builder.Configuration.GetConnectionString("AmbrosiaContext") ?? string.Empty,
    Auth = builder.Configuration.GetSection("Auth").Get<Ambrosia.DTO.Settings.AuthSettings>()
            ?? new Ambrosia.DTO.Settings.AuthSettings(),
    CpfProtection = builder.Configuration.GetSection("CpfProtection")
            .Get<Ambrosia.DTO.Settings.CpfProtectionSettings>()
            ?? new Ambrosia.DTO.Settings.CpfProtectionSettings()
};

// Falha no startup em vez de rodar com chave vazia: CPFs cifrados com chave errada são
// irrecuperáveis e tokens assinados por segredo fraco não dão autenticidade (research §16).
ValidateSettings(settings);

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
// Autenticação/autorização: JWT Bearer HS256 emitido pelo Ambrosia (constituição §VI,
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
        Title = "Ambrosia API",
        Version = "1.0.0",
        Description = "API de cadastro e consulta de clientes e pedidos (feature 001). " +
                      "Todas as rotas exceto `/auth/*` exigem `Authorization: Bearer <token>`. " +
                      "Cada usuário enxerga apenas os próprios clientes e pedidos; recursos de " +
                      "outro usuário respondem 404."
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Cole o token do Ambrosia obtido em POST /auth/google (ou /auth/dev-login " +
                      "em Development). Não inclua o prefixo \"Bearer\"."
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });

    var xmlPath = Path.Combine(AppContext.BaseDirectory, $"{typeof(Program).Assembly.GetName().Name}.xml");
    if (File.Exists(xmlPath))
    {
        options.IncludeXmlComments(xmlPath);
    }

    // T047: /auth/dev-login só aparece na documentação em Development, exatamente como o
    // comportamento da rota (research §2).
    options.DocumentFilter<DevLoginDocumentFilter>();
});

// ---------------------------------------------------------------------------
// CORS: AllowAnyOrigin SOMENTE em Development (constituição §VI).
// ---------------------------------------------------------------------------
const string CorsPolicyName = "ambrosia-dev";

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddCors(options => options.AddPolicy(
        CorsPolicyName,
        policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));
}

var app = builder.Build();

// Swagger UI em todos os ambientes: a documentação não contém dados sensíveis (research §14).
app.UseSwagger();
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
static void ValidateSettings(AmbrosiaSettings settings)
{
    var errors = new List<string>();

    if (string.IsNullOrWhiteSpace(settings.ConnectionString))
    {
        errors.Add("ConnectionStrings__AmbrosiaContext está vazia.");
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

    if (errors.Count > 0)
    {
        throw new InvalidOperationException(
            "Configuração inválida da API Ambrosia: " + string.Join(" ", errors) +
            " Defina as variáveis de ambiente ou o appsettings antes de iniciar.");
    }
}
