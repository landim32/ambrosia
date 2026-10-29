using System.Collections.Concurrent;
using Ambrosio.Domain.Common;
using Ambrosio.Domain.Interfaces;
using Ambrosio.Domain.Models;
using Ambrosio.DTO.Settings;
using Ambrosio.Infra.Interfaces.Repository;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ambrosio.Application.HostedServices;

/// <summary>
/// Executor das importações (feature 006, research §1/§4; fila reescrita pela feature 008, R9): pega
/// as execuções <c>queued</c> do banco, limita a concorrência e roda cada uma em escopo de DI próprio,
/// no mesmo padrão do <c>PendingImageCleanupService</c>.
/// <para>
/// <b>A fila é o banco, não a memória deste processo</b>. Até a 006 o <c>POST /runs</c> enfileirava um
/// <see cref="ImportJob"/> num <c>Channel</c> do próprio processo porque o job carregava a senha do
/// +Vendas — persisti-la seria o vazamento que a FR-006 proibia. Com as credenciais cifradas em
/// <c>mais_vendas_accounts</c> (008), o job só carrega a chave da execução, e a fila pode ser a própria
/// tabela: é o que permite ao host do MCP iniciar uma importação sem navegador e sem worker (o plugin
/// grava a linha <c>queued</c>; este processo a reivindica), e é o mesmo caminho que o cancelamento já
/// fazia pelo banco. A reivindicação é atômica (<c>queued → running</c> condicional), então duas réplicas
/// da API não pegariam a mesma execução.
/// </para>
/// <para>
/// <b>Recuperação no arranque (FR-020)</b>: <see cref="ExecuteAsync"/> começa chamando
/// <c>MarkInterruptedAsync</c> — execução <c>queued</c>/<c>running</c> que sobrou de um processo anterior
/// não tem mais navegador, e deixá-la "em andamento" mentiria para a vendedora e bloquearia novos inícios
/// pelo índice único. A chamada é escrita em lote e não lê nada (constituição §VI); este é o único
/// chamador dela, e a reivindicação é a outra exceção documentada (§VI): devolve só a chave da execução,
/// nunca dado de usuário.
/// </para>
/// </summary>
public class ImportRunWorker : BackgroundService
{
    /// <summary>
    /// Intervalo da varredura por execuções na fila. Um segundo é o compromisso entre a tela (que mostra
    /// a execução em 5 s de qualquer forma, SC-002 da 008) e o banco (uma consulta por segundo por
    /// processo, atendido por <c>ix_import_runs_user_requested</c> no caminho comum).
    /// </summary>
    private static readonly TimeSpan QueuePollInterval = TimeSpan.FromSeconds(1);

    private readonly IServiceScopeFactory _scopeFactory;
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
        IOptions<MaisVendasSettings> settings,
        ILogger<ImportRunWorker> logger)
    {
        _scopeFactory = scopeFactory;
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
            "Worker de importação pronto: {Slots} execução(ões) simultânea(s), coletor {Collector}, fila no banco a cada {Poll}s.",
            _settings.MaxConcurrentRuns,
            _settings.IsFake ? "fake" : "playwright",
            QueuePollInterval.TotalSeconds);

        using var timer = new PeriodicTimer(QueuePollInterval);

        try
        {
            do
            {
                await PollQueueAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
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
    /// Enche as vagas livres com execuções da fila. Só pede uma por vez ao banco, e devolve a vaga se
    /// não havia nada: é assim que "aguardando" (FR-017) continua sendo o estado visível de quem ainda
    /// não tem vaga, em vez de um lote reivindicado e parado em memória.
    /// </summary>
    private async Task PollQueueAsync(CancellationToken stoppingToken)
    {
        var slots = _slots!;

        while (!stoppingToken.IsCancellationRequested && await slots.WaitAsync(TimeSpan.Zero, stoppingToken))
        {
            ImportJob? job;

            try
            {
                job = await ClaimNextJobAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                // Banco inacessível na varredura: devolve a vaga e tenta no próximo tick. Um worker que
                // morresse aqui pararia as importações do processo até o reinício do host.
                _logger.LogWarning(ex, "Não foi possível procurar a próxima execução na fila.");
                slots.Release();

                return;
            }

            if (job is null)
            {
                slots.Release();

                return;
            }

            _ = Task.Run(() => ConsumeAsync(job, stoppingToken), stoppingToken);
        }
    }

    /// <summary>
    /// Reivindica a execução <c>queued</c> mais antiga (FR-016/FR-017, R9 da 008) e monta o trabalho.
    /// A transição <c>queued → running</c> é atômica no repositório, então uma segunda varredura — desta
    /// réplica ou de outra — nunca pega a mesma linha.
    /// </summary>
    private async Task<ImportJob?> ClaimNextJobAsync(CancellationToken stoppingToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();

        var runs = scope.ServiceProvider
            .GetRequiredService<IImportRunRepository<ImportRun, ImportRunBlock, ImportRunChange>>();

        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        stoppingToken.ThrowIfCancellationRequested();

        // Exceção à §VI, registrada aqui e no repositório: é a única leitura sem filtro por usuário do
        // sistema, é a fila, e devolve a linha da execução — nenhum dado de cliente. Quem executa relê a
        // execução pelo dono (GetByIdAsync(id, userId)) antes de escrever qualquer coisa.
        var run = await runs.ClaimNextQueuedRunAsync(clock.UtcNow);

        if (run is null)
        {
            return null;
        }

        // O job não carrega credencial nenhuma desde a 008: o executor as decifra por usuário no
        // instante do login na origem (research R3/R7).
        return new ImportJob
        {
            RunId = run.Id,
            UserId = run.UserId,
            Mode = run.Mode,
            StartDate = run.StartDate,
            EndDate = run.EndDate
        };
    }

    /// <summary>
    /// Uma execução reivindicada do começo ao fim: checa se ainda vale a pena rodar, executa em escopo
    /// próprio e devolve a vaga.
    /// </summary>
    private async Task ConsumeAsync(ImportJob job, CancellationToken stoppingToken)
    {
        var slots = _slots!;

        using var scope = _logger.BeginScope(new Dictionary<string, object> { ["RunId"] = job.RunId });

        // O CTS é registrado antes de rodar: é o que permite ao shutdown sinalizar o bloco em curso.
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        _running[job.RunId] = cancellation;

        try
        {
            if (await ShouldSkipAsync(job, stoppingToken))
            {
                // Sumiu do banco ou já foi encerrada (cancelamento em queued, recuperação do arranque):
                // nenhum navegador é aberto (FR-019).
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

            // FR-005 da 006 / FR-003 da 008: o job não carrega segredo, e mesmo assim é zerado — se
            // alguém reintroduzir uma credencial na classe, o finally continua sendo o ponto único.
            job.Clear();
        }
    }

    /// <summary>
    /// Relê o status da execução antes de começar de fato. Leitura filtrada por usuário (a do próprio
    /// job), e só interessa "ainda existe e ainda está ativa".
    /// <para>
    /// <c>CancelRequested</c> <b>não</b> é motivo para pular aqui: depois da reivindicação a execução já
    /// é <c>running</c>, e um cancelamento chegado nesse intervalo precisa ser atendido pelo executor —
    /// que fecha como <c>cancelled</c> — e não abandonado aqui, o que a deixaria "em andamento" para
    /// sempre sem ninguém rodando.
    /// </para>
    /// </summary>
    private async Task<bool> ShouldSkipAsync(ImportJob job, CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();

            var runs = scope.ServiceProvider
                .GetRequiredService<IImportRunRepository<ImportRun, ImportRunBlock, ImportRunChange>>();

            // Leitura com filtro pelo dono do próprio job (§VI): é a execução daquela vendedora, nunca
            // uma consulta por id solto. Null = sumiu; status final = alguém já encerrou.
            var run = await runs.GetByIdAsync(job.RunId, job.UserId);

            return run is null || !run.IsActive;
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
