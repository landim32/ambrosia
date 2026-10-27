using System.Collections.Concurrent;
using Ambrosia.Domain.Common;
using Ambrosia.Domain.Interfaces;
using Ambrosia.Domain.Models;
using Ambrosia.DTO.Settings;
using Ambrosia.Infra.Interfaces.Repository;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ambrosia.Application.HostedServices;

/// <summary>
/// Consumidor da fila de importação (feature 006, research §1/§4): lê o <see cref="IImportRunQueue"/>,
/// limita a concorrência e roda cada execução em escopo de DI próprio, no mesmo padrão do
/// <c>PendingImageCleanupService</c>.
/// <para>
/// <b>Recuperação no arranque (FR-020)</b>: <see cref="ExecuteAsync"/> começa chamando
/// <c>MarkInterruptedAsync</c> — execução <c>queued</c>/<c>running</c> que sobrou de um processo
/// anterior não tem mais navegador nem fila, e deixá-la "em andamento" mentiria para a vendedora e
/// bloquearia novos inícios pelo índice único. A chamada é escrita em lote e não lê nada (constituição
/// §VI); este é o único chamador dela.
/// </para>
/// <para>
/// <b>Credenciais</b>: o <see cref="ImportJob"/> é a única referência ao login/senha. Ele nunca entra
/// no estado do log — os logs usam <c>BeginScope(new { RunId })</c> — e é zerado no <c>finally</c> com
/// sucesso, falha ou cancelamento (FR-005).
/// </para>
/// </summary>
public class ImportRunWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IImportRunQueue _queue;
    private readonly ILogger<ImportRunWorker> _logger;
    private readonly MaisVendasSettings _settings;

    /// <summary>
    /// Token de cancelamento por execução em curso. Serve ao shutdown do host (derruba o navegador do
    /// bloco atual) e ao cancelamento pedido pela vendedora; a execução observa
    /// <c>cancel_requested</c> no banco entre blocos (FR-019).
    /// </summary>
    private readonly ConcurrentDictionary<long, CancellationTokenSource> _running = new();

    public ImportRunWorker(
        IServiceScopeFactory scopeFactory,
        IImportRunQueue queue,
        IOptions<MaisVendasSettings> settings,
        ILogger<ImportRunWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _queue = queue;
        _settings = settings.Value;
        _logger = logger;
    }

    /// <summary>Um semáforo para todas as execuções: é o que "aguardando" significa na tela (FR-017).</summary>
    private SemaphoreSlim? _slots;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _slots = new SemaphoreSlim(_settings.MaxConcurrentRuns, _settings.MaxConcurrentRuns);

        await MarkInterruptedRunsAsync();

        _logger.LogInformation(
            "Worker de importação pronto: {Slots} execução(ões) simultânea(s), coletor {Collector}.",
            _settings.MaxConcurrentRuns,
            _settings.IsFake ? "fake" : "playwright");

        try
        {
            await foreach (var job in _queue.ReadAllAsync(stoppingToken))
            {
                // Uma tarefa por job: o semáforo é o que limita, e a fila continua sendo lida enquanto
                // uma execução roda (o contrário congestionaria a tela inteira).
                _ = Task.Run(() => ConsumeAsync(job, stoppingToken), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown do host: normal.
        }
    }

    /// <summary>
    /// FR-020. Executado antes de qualquer job: uma falha aqui não derruba o host — sem o banco a API
    /// inteira está inutilizável de qualquer forma, e o log registra.
    /// </summary>
    private async Task MarkInterruptedRunsAsync()
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();

            var clock = scope.ServiceProvider.GetRequiredService<IClock>();

            // Único chamador de MarkInterruptedAsync (revisão de segurança T111): escrita em lote, sem
            // leitura, sem retorno de dado de usuário — por isso a exceção explícita à §VI é sua.
            var runs = scope.ServiceProvider.GetRequiredService<IImportRunRepository<ImportRun, ImportRunBlock, ImportRunChange>>();

            var affected = await runs.MarkInterruptedAsync(clock.UtcNow);

            if (affected > 0)
            {
                _logger.LogInformation(
                    "Importação interrompida pela reinicialização: {Affected} linha(s) ajustada(s) (FR-020).",
                    affected);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Não foi possível marcar execuções interrompidas no arranque.");
        }
    }

    /// <summary>
    /// Um job do começo ao fim: espera a vaga, checa se ainda vale a pena rodar, executa em escopo
    /// próprio e descarta as credenciais.
    /// </summary>
    private async Task ConsumeAsync(ImportJob job, CancellationToken stoppingToken)
    {
        var slots = _slots!;

        using var scope = _logger.BeginScope(new Dictionary<string, object> { ["RunId"] = job.RunId });

        try
        {
            await slots.WaitAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            job.Clear();

            return;
        }

        // O CTS é registrado antes de rodar: é o que permite ao shutdown sinalizar o bloco em curso.
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        _running[job.RunId] = cancellation;

        try
        {
            if (await ShouldSkipAsync(job, stoppingToken))
            {
                // Cancelada enquanto aguardava a vaga, ou já encerrada por outro caminho: o job é
                // descartado sem abrir navegador nenhum (FR-019).
                return;
            }

            await using var executionScope = _scopeFactory.CreateAsyncScope();

            var execution = executionScope.ServiceProvider.GetRequiredService<IImportExecutionService>();

            await execution.ExecuteAsync(job, cancellation.Token);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Execução {RunId} interrompida pelo encerramento do host.", job.RunId);
        }
        catch (Exception ex)
        {
            // O worker é um serviço de fundo: nada aqui pode derrubar o host. O estado final da
            // execução já foi escrito pelo serviço (failed); isto é o registro do inesperado.
            _logger.LogWarning(ex, "Execução {RunId} não concluiu o fluxo normal.", job.RunId);
        }
        finally
        {
            _running.TryRemove(job.RunId, out _);
            slots.Release();

            // FR-005: em sucesso, falha ou cancelamento, as referências ao login/senha somem.
            job.Clear();
        }
    }

    /// <summary>
    /// Relê o status da execução antes de começar de fato. Leitura filtrada por usuário (a do próprio
    /// job), e só interessa "ainda está ativa e ninguém pediu para cancelar".
    /// </summary>
    private async Task<bool> ShouldSkipAsync(ImportJob job, CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();

            var runs = scope.ServiceProvider
                .GetRequiredService<IImportRunRepository<ImportRun, ImportRunBlock, ImportRunChange>>();

            // Leitura com filtro pelo dono do próprio job (§VI): é a execução daquela vendedora, nunca
            // uma consulta por id solto. Null = sumiu; CancelRequested = a vendedora desistiu antes de
            // começar; status final = alguém já encerrou (reinício, cancelamento em queued).
            var run = await runs.GetByIdAsync(job.RunId, job.UserId);

            return run is null || run.CancelRequested || !run.IsActive;
        }
        catch (Exception ex)
        {
            // Sem resposta do banco, o melhor é tentar: o serviço trata a falha e fecha a execução.
            _logger.LogWarning(ex, "Não foi possível confirmar o estado da execução {RunId}.", job.RunId);

            return false;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // Encerramento ordenado: sinaliza as execuções em curso para que o navegador seja fechado
        // (DisposeAsync da sessão) antes de o host terminar.
        foreach (var source in _running.Values)
        {
            source.Cancel();
        }

        await base.StopAsync(cancellationToken);
    }

    public override void Dispose()
    {
        _slots?.Dispose();

        base.Dispose();
    }
}
