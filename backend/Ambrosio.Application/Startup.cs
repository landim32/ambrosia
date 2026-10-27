using Ambrosio.Application.HostedServices;
using Ambrosio.Application.Imports;
using Ambrosio.Application.Settings;
using Ambrosio.Domain.Common;
using Ambrosio.Domain.Exceptions;
using Ambrosio.Domain.Interfaces;
using Ambrosio.Domain.Models;
using Ambrosio.Domain.Services;
using Ambrosio.DTO.Settings;
using Ambrosio.Infra.AppServices;
using Ambrosio.Infra.AppServices.MaisVendas;
using Ambrosio.Infra.Context;
using Ambrosio.Infra.Interfaces.AppServices;
using Ambrosio.Infra.Interfaces.Repository;
using Ambrosio.Infra.Repository;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Ambrosio.Application;

/// <summary>
/// Composition root da solução (skill <c>dotnet-architecture</c>): todo o registro de DI do
/// backend acontece aqui, em um único lugar. As histórias acrescentam seus próprios blocos
/// (Repositories / AppServices / Domain Services) nas seções âncora abaixo.
/// </summary>
public static class Startup
{
    public static IServiceCollection ConfigureServices(
        this IServiceCollection services,
        Action<AmbrosioSettings> configureSettings)
    {
        var settings = new AmbrosioSettings();
        configureSettings(settings);

        // IOptions consumidos pela Infra (JwtTokenAppService, GoogleOAuthTokenAppService,
        // CpfProtectionAppService). As classes ficam em Ambrosio.DTO/Settings.
        services.Configure<AuthSettings>(opt =>
        {
            opt.JwtSecret = settings.Auth.JwtSecret;
            opt.Issuer = settings.Auth.Issuer;
            opt.Audience = settings.Auth.Audience;
            opt.ExpirationMinutes = settings.Auth.ExpirationMinutes;
            opt.GoogleClientId = settings.Auth.GoogleClientId;
        });

        services.Configure<CpfProtectionSettings>(opt =>
        {
            opt.Pepper = settings.CpfProtection.Pepper;
            opt.EncryptionKey = settings.CpfProtection.EncryptionKey;
        });

        // Storage de imagens (feature 004, research §12): os mesmos campos copiados um a um, para
        // que IOptions<ObjectStorageSettings> seja consumível pela Infra, pelo ImageService e pelo
        // job de limpeza sem depender do objeto agregador.
        services.Configure<ObjectStorageSettings>(opt =>
        {
            opt.ServiceUrl = settings.ObjectStorage.ServiceUrl;
            opt.PublicServiceUrl = settings.ObjectStorage.PublicServiceUrl;
            opt.Region = settings.ObjectStorage.Region;
            opt.Bucket = settings.ObjectStorage.Bucket;
            opt.ForcePathStyle = settings.ObjectStorage.ForcePathStyle;
            opt.AccessKey = settings.ObjectStorage.AccessKey;
            opt.SecretKey = settings.ObjectStorage.SecretKey;
            opt.PresignedReadMinutes = settings.ObjectStorage.PresignedReadMinutes;
            opt.PresignedUploadMinutes = settings.ObjectStorage.PresignedUploadMinutes;
            opt.MaxUploadBytes = settings.ObjectStorage.MaxUploadBytes;
            opt.AllowedMimeTypes = settings.ObjectStorage.AllowedMimeTypes;
            opt.PendingTtlMinutes = settings.ObjectStorage.PendingTtlMinutes;
            opt.CleanupIntervalMinutes = settings.ObjectStorage.CleanupIntervalMinutes;
        });

        // Importação do +Vendas (feature 006, research §13): a seção inteira vai adiante como IOptions,
        // consumida pelo coletor, pelos serviços de importação, pelo worker e pelo validator de início.
        services.Configure<MaisVendasSettings>(opt =>
        {
            opt.Collector = settings.MaisVendas.Collector;
            opt.BaseUrl = settings.MaisVendas.BaseUrl;
            opt.BlockDays = settings.MaisVendas.BlockDays;
            opt.MaxRangeDays = settings.MaisVendas.MaxRangeDays;
            opt.MaxConcurrentRuns = settings.MaisVendas.MaxConcurrentRuns;
            opt.MaxFailedLogins = settings.MaisVendas.MaxFailedLogins;
            opt.LockoutMinutes = settings.MaisVendas.LockoutMinutes;
            opt.MinDelayMs = settings.MaisVendas.MinDelayMs;
            opt.MaxDelayMs = settings.MaisVendas.MaxDelayMs;
            opt.Fake.DelayMs = settings.MaisVendas.Fake.DelayMs;
        });

        // DbContext (EF Core 9 + Npgsql, sobre .NET 8 — research §18).
        services.AddDbContext<AmbrosioContext>(options => options.UseNpgsql(settings.ConnectionString));

        // Relógio injetável: permite testes com "hoje" controlável (tasks.md, ajuste ao research §6).
        services.AddSingleton<IClock, SystemClock>();

        // Validators FluentValidation do Domain (skill dotnet-fluent-validation).
        services.AddValidatorsFromAssembly(typeof(NotFoundException).Assembly);

        // Repositories
        services.AddScoped<IUserRepository<User>, UserRepository>();
        services.AddScoped<ICustomerRepository<Customer>, CustomerRepository>();
        services.AddScoped<IOrderRepository<Order>, OrderRepository>();
        services.AddScoped<IStoredImageRepository<StoredImage>, StoredImageRepository>();

        // AppServices
        services.AddScoped<IOAuthTokenAppService, GoogleOAuthTokenAppService>();
        services.AddSingleton<IJwtTokenAppService, JwtTokenAppService>();
        services.AddSingleton<ICpfProtectionAppService, CpfProtectionAppService>();
        services.AddSingleton<IPasswordHashAppService, PasswordHashAppService>();

        // Storage de imagens (feature 004): os dois clientes S3 não guardam estado por requisição — o AmazonS3Client é thread-safe e caro (conexões, cache de região),
        // então singleton. As URLs pré-assinadas são derivadas do relógio injetado, nunca de estado.
        services.AddSingleton<IObjectStorageAppService, ObjectStorageAppService>();

        // Domain Services
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<IImageService, ImageService>();

        // Job de limpeza de envios abandonados (FR-018, research §9): abre um escopo por ciclo, então
        // só precisa do IServiceScopeFactory singleton daqui.
        services.AddHostedService<PendingImageCleanupService>();

        // ---------------------------------------------------------------------------
        // Importação do +Vendas (feature 006)
        // ---------------------------------------------------------------------------

        services.AddScoped<IImportRunRepository<ImportRun, ImportRunBlock, ImportRunChange>, ImportRunRepository>();
        services.AddScoped<IMaisVendasAccountRepository<MaisVendasAccount>, MaisVendasAccountRepository>();

        // Hash da conta com o mesmo pepper do CPF, mas AppService próprio: a semântica é diferente
        // (comparação, sem recuperação) e ICpfProtectionAppService continua exclusivo do documento
        // (research §8). Singleton: só deriva digest a partir do segredo configurado.
        services.AddSingleton<IAccountHashAppService, AccountHashAppService>();

        // O coletor é escolhido pela configuração (research §3): Fake é a origem de teste controlada da
        // homologação; Playwright é o navegador real. Program.ValidateSettings recusa Fake em
        // Production, então a outra saída é sempre o coletor real.
        if (settings.MaisVendas.IsFake)
        {
            services.AddSingleton<IMaisVendasCollectorAppService, FakeMaisVendasCollectorAppService>();
        }
        else
        {
            // Limitador de ritmo compartilhado com o download das fotos (FR-043): singleton porque o
            // bucket precisa ser um só por processo — duas instâncias dobrariam o ritmo real.
            services.AddSingleton<OriginRateLimiter>();
            services.AddSingleton<IMaisVendasCollectorAppService, PlaywrightMaisVendasCollectorAppService>();
        }

        // Fila em memória: singleton porque é o canal entre a requisição (que enfileira) e o worker
        // (que consome). O ImportJob não é registrado como serviço — ele carrega a senha.
        services.AddSingleton<IImportRunQueue, ImportRunQueue>();

        services.AddScoped<IImportRunService, ImportRunService>();

        // O executor é scoped: cada job roda em escopo próprio (research §7), com DbContext e
        // repositórios novos, e é o único consumidor destas dependências.
        services.AddScoped<IImportExecutionService, ImportExecutionService>();

        services.AddHostedService<ImportRunWorker>();

        return services;
    }
}
