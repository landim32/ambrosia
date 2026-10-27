using Ambrosia.Domain.Interfaces;
using Ambrosia.DTO.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ambrosia.Application.HostedServices;

/// <summary>
/// Job que remove envios abandonados (FR-018, research §9): a cada <c>CleanupIntervalMinutes</c> abre
/// um escopo novo e chama <see cref="IImageService.PurgeStaleAsync"/>, que apaga pendentes e falhas com
/// mais de <c>PendingTtlMinutes</c>.
/// <para>
/// Existe porque o envio pelo navegador tem dois momentos que podem nunca se encontrar: o cliente
/// declara, pega a URL e nunca faz o <c>PUT</c> (ou faz e não confirma). Sem esta varredura, o bucket
/// acumulava binário sem dono e a tabela acumulava linhas que nunca ficam disponíveis — a limpeza é o
/// preço de aceitar <c>PUT</c> direto no storage, que não limita o tamanho do corpo (HU 003, risco).
/// </para>
/// <para>
/// Nada aqui derruba o host: uma falha de storage num ciclo é registrada e tentada no próximo. O log
/// não menciona chave, bucket nem URL (FR-021).
/// </para>
/// </summary>
public class PendingImageCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PendingImageCleanupService> _logger;
    private readonly ObjectStorageSettings _settings;

    public PendingImageCleanupService(
        IServiceScopeFactory scopeFactory,
        IOptions<ObjectStorageSettings> settings,
        ILogger<PendingImageCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(_settings.CleanupIntervalMinutes);

        using var timer = new PeriodicTimer(interval);

        _logger.LogInformation(
            "Limpeza de imagens abandonadas agendada a cada {Interval} minutos (TTL {Ttl} minutos).",
            _settings.CleanupIntervalMinutes, _settings.PendingTtlMinutes);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await PurgeOnceAsync(stoppingToken);
        }
    }

    /// <summary>
    /// Um ciclo. IImageService é scoped (pede DbContext), por isso o escopo novo por execução — o
    /// <see cref="BackgroundService"/> é singleton e nunca segura o contexto da requisição alheia.
    /// </summary>
    private async Task PurgeOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();

            var imageService = scope.ServiceProvider.GetRequiredService<IImageService>();
            var clock = scope.ServiceProvider.GetRequiredService<IClock>();

            await imageService.PurgeStaleAsync(clock.UtcNow);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown do host: não é pane, é o processo encerrando.
        }
        catch (Exception ex)
        {
            // Storage fora do ar, banco ocupado — o próximo ciclo tenta de novo (research §9). Nunca
            // se propaga: derrubaria a API inteira por um job de fundo.
            _logger.LogWarning(ex, "Ciclo de limpeza de imagens não concluído; nova tentativa no próximo intervalo.");
        }
    }
}
