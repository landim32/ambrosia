using Ambrosia.Application.HostedServices;
using Ambrosia.Application.Settings;
using Ambrosia.Domain.Common;
using Ambrosia.Domain.Exceptions;
using Ambrosia.Domain.Interfaces;
using Ambrosia.Domain.Models;
using Ambrosia.Domain.Services;
using Ambrosia.DTO.Settings;
using Ambrosia.Infra.AppServices;
using Ambrosia.Infra.Context;
using Ambrosia.Infra.Interfaces.AppServices;
using Ambrosia.Infra.Interfaces.Repository;
using Ambrosia.Infra.Repository;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Ambrosia.Application;

/// <summary>
/// Composition root da solução (skill <c>dotnet-architecture</c>): todo o registro de DI do
/// backend acontece aqui, em um único lugar. As histórias acrescentam seus próprios blocos
/// (Repositories / AppServices / Domain Services) nas seções âncora abaixo.
/// </summary>
public static class Startup
{
    public static IServiceCollection ConfigureServices(
        this IServiceCollection services,
        Action<AmbrosiaSettings> configureSettings)
    {
        var settings = new AmbrosiaSettings();
        configureSettings(settings);

        // IOptions consumidos pela Infra (JwtTokenAppService, GoogleOAuthTokenAppService,
        // CpfProtectionAppService). As classes ficam em Ambrosia.DTO/Settings.
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

        // DbContext (EF Core 9 + Npgsql, sobre .NET 8 — research §18).
        services.AddDbContext<AmbrosiaContext>(options => options.UseNpgsql(settings.ConnectionString));

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

        // Storage de imagens (feature 004): os dois clientes S3 e o processador de imagem não guardam
        // estado por requisição — o AmazonS3Client é thread-safe e caro (conexões, cache de região),
        // então singleton. As URLs pré-assinadas são derivadas do relógio injetado, nunca de estado.
        services.AddSingleton<IObjectStorageAppService, ObjectStorageAppService>();
        services.AddSingleton<IImageProcessingAppService, ImageProcessingAppService>();

        // Domain Services
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<IImageService, ImageService>();

        // Job de limpeza de envios abandonados (FR-018, research §9): abre um escopo por ciclo, então
        // só precisa do IServiceScopeFactory singleton daqui.
        services.AddHostedService<PendingImageCleanupService>();

        return services;
    }
}
