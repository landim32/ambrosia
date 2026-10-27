using Ambrosia.Domain.Common;
using Ambrosia.Domain.Exceptions;
using Ambrosia.Domain.Interfaces;
using Ambrosia.Domain.Models;
using Ambrosia.Domain.ValueObjects;
using Ambrosia.DTO.Enums;
using Ambrosia.DTO.Imports;
using Ambrosia.DTO.Imports.Collector;
using Ambrosia.DTO.Settings;
using Ambrosia.Infra.Interfaces.AppServices;
using Ambrosia.Infra.Interfaces.Repository;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ambrosia.Domain.Services;

/// <summary>
/// Orquestração de uma execução de importação (feature 006, research §1/§3/§6/§7/§8/§12/§17/§18). É o
/// coração da feature e o único lugar que decide o que acontece com cada pedido da origem.
/// <para>
/// Sequência: abre a sessão e autentica; resolve vínculo de conta e recusas; para cada bloco — coletar,
/// <b>validar contadores e totais antes de gravar</b> (FR-010/FR-011), gravar pedido a pedido em escopo
/// de DI próprio (FR-012), copiar fotos, reportar não encontrados, atualizar o progresso; ao final
/// aplicar a FR-047a (research §18) e fechar. A sessão é descartada no <c>finally</c> em todo caminho
/// (FR-044) e o job tem as credenciais zeradas pelo worker.
/// </para>
/// <para>
/// <b>Credenciais e CPF (FR-005/FR-052)</b>: login e senha só existem no <see cref="ImportJob"/> e na
/// sessão; o CPF só existe em <see cref="CollectedOrder.CpfDigits"/> até virar hash/cifra/máscara.
/// Nenhuma mensagem escrita aqui repete um dos três, porque um <c>Exception</c> não tratado do pipeline
/// é devolvido como <c>500</c> com <c>ex.Message</c>.
/// </para>
/// </summary>
public class ImportExecutionService : IImportExecutionService
{
    // Mensagens fixas do contrato (contracts/imports-api.md), sem dado pessoal.
    private const string REFUSED_CREDENTIALS = "Credenciais recusadas pelo +Vendas.";
    private const string DIFFERENT_ACCOUNT = "Conta do +Vendas diferente da vinculada.";
    private const string UNSUPPORTED_STEP = "O login do +Vendas exige uma etapa não suportada (código ou captcha).";
    private const string INVALID_CPF = "CPF ausente ou inválido na origem.";

    /// <summary>Retentativas do bloco (FR-045): 2 s → 4 s → 8 s, e a 3ª falha aborta.</summary>
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4),
        TimeSpan.FromSeconds(8)
    ];

    private readonly IImportRunRepository<ImportRun, ImportRunBlock, ImportRunChange> _runs;
    private readonly IMaisVendasAccountRepository<MaisVendasAccount> _accounts;
    private readonly IMaisVendasCollectorAppService _collector;
    private readonly IAccountHashAppService _accountHash;
    private readonly ICpfProtectionAppService _cpfProtection;
    private readonly IImageService _images;
    private readonly IClock _clock;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MaisVendasSettings _settings;
    private readonly ObjectStorageSettings _storage;
    private readonly ILogger<ImportExecutionService> _logger;

    public ImportExecutionService(
        IImportRunRepository<ImportRun, ImportRunBlock, ImportRunChange> runs,
        IMaisVendasAccountRepository<MaisVendasAccount> accounts,
        IMaisVendasCollectorAppService collector,
        IAccountHashAppService accountHash,
        ICpfProtectionAppService cpfProtection,
        IImageService images,
        IClock clock,
        IServiceScopeFactory scopeFactory,
        IOptions<MaisVendasSettings> settings,
        IOptions<ObjectStorageSettings> storage,
        ILogger<ImportExecutionService> logger)
    {
        _runs = runs;
        _accounts = accounts;
        _collector = collector;
        _accountHash = accountHash;
        _cpfProtection = cpfProtection;
        _images = images;
        _clock = clock;
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
        _storage = storage.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(ImportJob job, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);

        var run = await _runs.GetByIdAsync(job.RunId, job.UserId);

        if (run is null || !run.IsActive)
        {
            // Sumiu do banco ou já foi encerrada (cancelamento em queued, recuperação do arranque).
            return;
        }

        run.Start(_clock.UtcNow);
        await _runs.UpdateAsync(run);

        var state = new RunState();

        // A sessão vive exatamente o tempo da execução; `await using` é o finally da FR-044 — ela é
        // descartada em sucesso, falha e cancelamento porque todo caminho sai deste bloco.
        await using var session = await _collector.OpenSessionAsync(
            new MaisVendasCredentials(job.Login, job.Password), cancellationToken);

        try
        {
            if (await LoginAndLinkAsync(run, job, session, state, cancellationToken))
            {
                await CollectBlocksAsync(run, session, state, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown do host no meio de um bloco: o que foi gravado fica, e a FR-020 do próximo
            // arranque não precisa corrigir nada — fechamos aqui mesmo como cancelada.
            run.RequestCancel();
            await FinishAsync(run);
        }
        catch (Exception ex)
        {
            // Nada daqui volta ao worker como exceção: a execução precisa de um status final e de uma
            // mensagem segura. O original vai para o log.
            _logger.LogWarning(ex, "Falha inesperada na execução {RunId}.", run.Id);

            if (run.Status is ImportRunStatus.Queued or ImportRunStatus.Running)
            {
                run.FailBeforeBlocks(SafeMessage(ex), _clock.UtcNow);
                await _runs.UpdateAsync(run);
            }
        }
    }

    // ------------------------------------------------------------------ login e vínculo

    /// <summary>
    /// Autentica e resolve o vínculo da conta (FR-007/FR-008a/FR-008b/FR-046).
    /// </summary>
    /// <returns><c>false</c> quando a execução terminou antes dos blocos — status e mensagem já gravados.</returns>
    private async Task<bool> LoginAndLinkAsync(
        ImportRun run,
        ImportJob job,
        IMaisVendasCollectorSession session,
        RunState state,
        CancellationToken cancellationToken)
    {
        var account = await _accounts.GetOrCreateAsync(run.UserId, _clock.UtcNow);
        var now = _clock.UtcNow;

        var result = await session.LoginAsync(cancellationToken);

        if (result.Outcome == MaisVendasLoginOutcome.Refused)
        {
            var locked = account.RegisterRefusedLogin(now, _settings.MaxFailedLogins, _settings.LockoutMinutes);

            await _accounts.UpdateAsync(account);

            _logger.LogWarning(
                "Credenciais recusadas na execução {RunId} ({Failed}ª recusa seguida{Locked}).",
                run.Id,
                account.FailedLoginCount,
                locked ? ", bloqueio aberto" : string.Empty);

            await FailBeforeBlocksAsync(run, REFUSED_CREDENTIALS);

            return false;
        }

        if (result.Outcome == MaisVendasLoginOutcome.UnsupportedStep)
        {
            // FR-046: captcha ou código 2FA. Não se tenta adivinhar e nada é gravado.
            await FailBeforeBlocksAsync(run, UNSUPPORTED_STEP);

            return false;
        }

        // Login aceito: a sequência de recusas acaba aqui, mesmo que a conta seja outra (FR-008a).
        account.RegisterAcceptedLogin(now);

        var identifier = string.IsNullOrWhiteSpace(result.AccountIdentifier) ? job.Login : result.AccountIdentifier!;
        var hash = _accountHash.Hash(identifier);

        if (account.AccountHash is { Length: > 0 } && !account.Matches(hash))
        {
            // FR-008b: outra conta do +Vendas para a mesma vendedora. Para ANTES de qualquer gravação
            // e também na simulação (T061: DryRun_DifferentAccount_AlsoFails) — a vendedora não pode
            // descobrir na hora de gravar que estava comparando com a conta errada.
            await _accounts.UpdateAsync(account);
            await FailBeforeBlocksAsync(run, DIFFERENT_ACCOUNT);

            _logger.LogWarning("Conta do +Vendas diferente da vinculada na execução {RunId}.", run.Id);

            return false;
        }

        // Primeiro vínculo: só em modo gravação (FR-032 — simulação não deixa rastro, T078
        // DryRun_DoesNotLinkAccount). O hash substitui o identificador: a coluna não tem login.
        if (account.AccountHash is null or { Length: 0 }
            && run.Mode == ImportRunMode.Apply)
        {
            account.Link(hash, now);
        }

        await _accounts.UpdateAsync(account);

        _logger.LogInformation(
            "Execução {RunId} autenticada no coletor {Collector} em modo {Mode}.",
            run.Id, _collector.CollectorVersion, run.Mode);

        return true;
    }

    private async Task FailBeforeBlocksAsync(ImportRun run, string reason)
    {
        run.FailBeforeBlocks(reason, _clock.UtcNow);

        await _runs.UpdateAsync(run);

        // O contrato exige blocks[*].status = notExecuted nessas falhas (imports-api.md): UpdateAsync
        // grava só os escalares da execução, então os blocos vão um a um.
        await PersistBlocksAsync(run);
    }

    /// <summary>
    /// Persiste o status de cada bloco carregado. Necessário porque <c>UpdateAsync</c> escreve só os
    /// escalares da execução (a escrita de progresso por pedido é <c>UpdateBlockAsync</c>), e transição
    /// de execução (falha antes dos blocos, cancelamento) altera blocos que nunca rodaram.
    /// </summary>
    private async Task PersistBlocksAsync(ImportRun run)
    {
        foreach (var block in run.Blocks)
        {
            await _runs.UpdateBlockAsync(block);
        }
    }

    // ------------------------------------------------------------------ blocos

    private async Task CollectBlocksAsync(
        ImportRun run,
        IMaisVendasCollectorSession session,
        RunState state,
        CancellationToken cancellationToken)
    {
        foreach (var block in run.Blocks.OrderBy(b => b.BlockNo))
        {
            // FR-019: o cancelamento é atendido entre blocos — o bloco em curso termina inteiro, porque
            // a metade de um bloco gravado é justamente o que a repetição precisa reencontrar.
            if (await IsCancelRequestedAsync(run, cancellationToken))
            {
                run.RequestCancel();

                break;
            }

            var ok = await RunBlockAsync(run, block, session, state, cancellationToken);

            if (!ok)
            {
                break;   // FR-013: bloco falho encerra a execução; os seguintes ficam notExecuted.
            }
        }

        await FinishAsync(run);
    }

    /// <summary>
    /// Um bloco por inteiro. Devolve <c>false</c> quando falhou (estado já escrito). A ordem é
    /// inegociável: coletar → <b>validar</b> → gravar (FR-010/FR-011 antes da FR-012).
    /// </summary>
    private async Task<bool> RunBlockAsync(
        ImportRun run,
        ImportRunBlock block,
        IMaisVendasCollectorSession session,
        RunState state,
        CancellationToken cancellationToken)
    {
        block.Start(_clock.UtcNow);
        await _runs.UpdateBlockAsync(block);

        using var _ = _logger.BeginScope(new Dictionary<string, object>
        {
            ["RunId"] = run.Id,
            ["BlockNo"] = block.BlockNo
        });

        var startedAt = _clock.UtcNow;
        CollectedBlock collected;

        try
        {
            collected = await CollectWithRetryAsync(session, block, cancellationToken);
        }
        catch (MaisVendasSourceException ex)
        {
            // FR-045: origem indisponível depois das retentativas. Nada deste bloco foi gravado.
            await FailBlockAsync(block, run, ex.Message, ex.Detail);

            return false;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            await FailBlockAsync(block, run, "Falha inesperada ao coletar o bloco na origem.", SafeMessage(ex));

            return false;
        }

        var elapsed = (_clock.UtcNow - startedAt).TotalSeconds;

        // Validação ANTES de gravar (SC-009): o que a origem anuncia × o que a lista realmente entrega.
        var divergence = BlockValidator.FindDivergence(collected);

        if (divergence is not null)
        {
            await FailBlockAsync(block, run, divergence, detail: null);

            return false;
        }

        // Pedidos já importados neste intervalo: os que não aparecerem na origem viram "missing" ao fim
        // do bloco, e só se o bloco terminou sem falha (FR-027, research §17).
        var importedBefore = await _runs.ListImportedKeysInRangeAsync(run.UserId, block.StartDate, block.EndDate);

        if (run.Mode == ImportRunMode.DryRun)
        {
            // A simulação não pode contar como "foto nova" uma URL que já está ligada a um item
            // existente (FR-032): as chaves do intervalo trazem as fotos já disponíveis.
            foreach (var key in importedBefore)
            {
                var linked = await _runs.GetLinkedImageIdsAsync(run.UserId, key.OrderNumber, key.OrderDate, key.Kind);

                foreach (var url in linked.Keys)
                {
                    state.AlreadyStoredUrls.Add(url);
                }
            }
        }

        var seen = new HashSet<(string, DateOnly, OrderKind)>();

        try
        {
            foreach (var order in collected.Orders)
            {
                seen.Add((order.OrderNumber.Trim(), order.OrderDate, order.Kind));

                await ProcessOrderAsync(run, block, order, session, state, cancellationToken);

                // FR-015: a tela acompanha com atraso ≤ 5 s — o bloco e a execução vão ao banco a cada
                // pedido, e não só no fim do bloco.
                await PersistProgressAsync(run, block);
            }
        }
        catch (ImportBlockWriteException ex)
        {
            // FR-012/FR-013: falha de gravação inesperada no meio do bloco. O que entrou fica gravado
            // (é o "14 pedidos gravados" do cenário falha-bloco3) e o bloco falha com a mensagem.
            await FailBlockAsync(block, run, ex.Message, ex.Detail);

            return false;
        }

        await ReportMissingAsync(run, block, importedBefore, seen);

        block.Succeed(_clock.UtcNow);
        await PersistProgressAsync(run, block);

        _logger.LogInformation(
            "Bloco {BlockNo} concluído em {Seconds:0} s: {Orders} pedido(s) lido(s), {Written} gravado(s).",
            block.BlockNo, elapsed, collected.Orders.Count, block.OrdersWritten);

        return true;
    }

    private async Task<CollectedBlock> CollectWithRetryAsync(
        IMaisVendasCollectorSession session,
        ImportRunBlock block,
        CancellationToken cancellationToken)
    {
        MaisVendasSourceException? last = null;

        for (var attempt = 0; attempt <= RetryDelays.Length; attempt++)
        {
            if (attempt > 0)
            {
                await Task.Delay(RetryDelays[attempt - 1], cancellationToken);
            }

            try
            {
                return await session.CollectBlockAsync(block.StartDate, block.EndDate, cancellationToken);
            }
            catch (MaisVendasSourceException ex) when (attempt < RetryDelays.Length)
            {
                last = ex;

                _logger.LogWarning(
                    "Coleta do bloco {BlockNo} falhou (tentativa {Attempt}/{Total}).",
                    block.BlockNo, attempt + 1, RetryDelays.Length + 1);
            }
        }

        throw last ?? new MaisVendasSourceException();
    }

    // ------------------------------------------------------------------ pedido a pedido

    /// <summary>
    /// CPF → proteção, hash do conteúdo, fotos e por fim o upsert em escopo próprio. Dado inválido
    /// conta <c>error</c> e segue (FR-036); falha de gravação inesperada derruba o bloco (FR-013).
    /// </summary>
    private async Task ProcessOrderAsync(
        ImportRun run,
        ImportRunBlock block,
        CollectedOrder order,
        IMaisVendasCollectorSession session,
        RunState state,
        CancellationToken cancellationToken)
    {
        var isDryRun = run.Mode == ImportRunMode.DryRun;

        // FR-036: sem CPF válido o pedido não entra — não existe cliente órfão nem documento inválido
        // protegido. O bloco continua e a execução fecha como parcial (FR-047a).
        if (!Cpf.TryParse(order.CpfDigits, out var cpf))
        {
            await AddChangeAsync(run, block, orderId: null, order, ImportChangeAction.Error, null, INVALID_CPF);

            block.Record(ImportChangeAction.Error);

            _logger.LogWarning(
                "Pedido {OrderNumber} de {OrderDate} sem CPF válido na origem.",
                order.OrderNumber, order.OrderDate);

            return;
        }

        var cpfHash = _cpfProtection.Hash(cpf!.Digits);
        var sourceHash = SourceHashCalculator.Compute(order, cpfHash);

        var imageIds = isDryRun
            ? CountPhotosThatWouldBeStored(block, order, state)
            : await StorePhotosAsync(run, block, order, session, state, cancellationToken);

        // Falha de gravação inesperada (ImportBlockWriteException) sobe daqui sem tratamento: é a
        // RunBlockAsync que fecha o bloco, porque ela é quem conhece os pedidos já gravados.
        var outcome = await UpsertOrderInOwnScopeAsync(run, order, cpf, cpfHash, sourceHash, imageIds);

        if (outcome.CustomerCreated && ShouldCountCustomer(isDryRun, cpfHash, state))
        {
            block.RecordCustomerCreated();
        }

        block.Record(outcome.Action);

        // Em simulação o orderId do rastro é nulo: nada foi gravado, e um link levaria a um pedido que
        // só existe na imaginação do relatório (contrato imports-api.md, T078).
        var changeOrderId = isDryRun ? null : outcome.OrderId;

        await AddChangeAsync(run, block, changeOrderId, order, outcome.Action, outcome.ChangedFields, null);

        if (outcome.Action == ImportChangeAction.Updated && outcome.ChangedFields is { Count: > 0 })
        {
            _logger.LogInformation(
                "Pedido {OrderNumber} atualizado: {ChangedFields}.",
                order.OrderNumber, string.Join(',', outcome.ChangedFields));
        }
    }

    /// <summary>
    /// Cliente criado sob demanda. Em gravação o repositório é a fonte (e a corrida devolve
    /// <c>false</c>); em simulação o conjunto em memória da execução é o que impede o mesmo CPF novo
    /// de ser contado três vezes em três pedidos (FR-032).
    /// </summary>
    private static bool ShouldCountCustomer(bool isDryRun, string cpfHash, RunState state)
    {
        if (!isDryRun)
        {
            return true;
        }

        return state.PreviewCreatedCpfHashes.Add(cpfHash);
    }

    /// <summary>
    /// Grava o pedido num <b>escopo de DI próprio</b> (research §7): um contexto novo por pedido, com
    /// transação curta. É o que faz o 15º pedido falho não desfazer os 14 anteriores.
    /// </summary>
    private async Task<UpsertOutcome> UpsertOrderInOwnScopeAsync(
        ImportRun run,
        CollectedOrder order,
        Cpf cpf,
        string cpfHash,
        string sourceHash,
        IReadOnlyDictionary<string, long> imageIds)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();

        var repository = scope.ServiceProvider
            .GetRequiredService<IImportRunRepository<ImportRun, ImportRunBlock, ImportRunChange>>();

        if (run.Mode == ImportRunMode.DryRun)
        {
            return await repository.PreviewImportedOrderAsync(run.UserId, order, cpfHash, sourceHash);
        }

        try
        {
            return await repository.UpsertImportedOrderAsync(
                run.UserId,
                order,
                cpfHash,
                _cpfProtection.Encrypt(cpf.Digits),
                cpf.Masked,
                sourceHash,
                imageIds,
                _clock.UtcNow);
        }
        catch (Exception ex) when (ex is not ImportBlockWriteException and not OperationCanceledException)
        {
            throw new ImportBlockWriteException(SafeMessage(ex));
        }
    }

    // ------------------------------------------------------------------ fotos

    /// <summary>
    /// Copia as fotos do pedido para o storage da vendedora (FR-037 a FR-041) <b>antes</b> do upsert:
    /// baixa com a sessão da coleta (as URLs pedem os cookies da vendedora) e grava como
    /// <c>ImageSource.Bot</c>. Toda falha de foto é contagem — o pedido segue gravado, sem imagem
    /// (FR-039).
    /// </summary>
    private async Task<Dictionary<string, long>> StorePhotosAsync(
        ImportRun run,
        ImportRunBlock block,
        CollectedOrder order,
        IMaisVendasCollectorSession session,
        RunState state,
        CancellationToken cancellationToken)
    {
        var imageIds = new Dictionary<string, long>(StringComparer.Ordinal);

        if (order.Items.Count == 0)
        {
            return imageIds;
        }

        // FR-040: as fotos já ligadas a este mesmo pedido com a mesma URL não são baixadas de novo —
        // é o caso de repetir o bloco que falhou depois de gravar a foto (FR-041).
        var alreadyLinked = await _runs.GetLinkedImageIdsAsync(
            run.UserId, order.OrderNumber.Trim(), order.OrderDate, order.Kind);

        foreach (var item in order.Items)
        {
            if (item.ImageUrl is not { Length: > 0 } url || imageIds.ContainsKey(url))
            {
                continue;
            }

            if (alreadyLinked.TryGetValue(url, out var existingId))
            {
                imageIds[url] = existingId;
                state.LinkedUrlToImageId[url] = existingId;

                continue;
            }

            if (state.LinkedUrlToImageId.TryGetValue(url, out var cachedId))
            {
                // Mesmo SKU em outro pedido da mesma execução: uma imagem, uma contagem (SC-010).
                imageIds[url] = cachedId;

                continue;
            }

            try
            {
                await using var downloaded = await session.DownloadImageAsync(
                    url, _storage.MaxUploadBytes, cancellationToken);

                await using var content = downloaded.Content;

                var (image, created) = await _images.StoreFromServerAsync(
                    run.UserId, content, downloaded.MimeType, ImageSource.Bot);

                imageIds[url] = image.ImageId;
                state.LinkedUrlToImageId[url] = image.ImageId;

                if (created)
                {
                    block.RecordImageStored();
                }
            }
            catch (ImageRejectedException ex)
            {
                // Tipo fora de AllowedMimeTypes (o gif do US6-3) ou tamanho excedido (FR-039).
                _logger.LogInformation("Foto do item {SkuRef} recusada: {Reason}", item.SkuRef, ex.Message);
                block.RecordImageFailed();
            }
            catch (MaisVendasSourceException)
            {
                // Toda falha de download de foto — inclusive origem indisponível (FR-045) — vira
                // "imagem com erro" e NÃO derruba o bloco (T090, FR-039/FR-045).
                block.RecordImageFailed();
            }
            catch (StorageUnavailableException)
            {
                block.RecordImageFailed();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha inesperada ao copiar a foto do item {SkuRef}.", item.SkuRef);
                block.RecordImageFailed();
            }
        }

        return imageIds;
    }

    /// <summary>
    /// Simulação (FR-032): nada é baixado nem gravado. <c>images_stored</c> conta as URLs distintas que
    /// <b>ainda não</b> estão ligadas a um item com a mesma URL — i.e. quantas fotos a gravação criaria.
    /// </summary>
    private static Dictionary<string, long> CountPhotosThatWouldBeStored(
        ImportRunBlock block,
        CollectedOrder order,
        RunState state)
    {
        foreach (var item in order.Items)
        {
            if (item.ImageUrl is not { Length: > 0 } url
                || !state.PreviewCountedUrls.Add(url)
                || state.AlreadyStoredUrls.Contains(url))
            {
                continue;
            }

            block.RecordImageStored();
        }

        return new Dictionary<string, long>(StringComparer.Ordinal);
    }

    // ------------------------------------------------------------------ rastro e fechamento

    /// <summary>
    /// Pedidos importados antes, ausentes da origem neste bloco (FR-027): viram <c>missing</c> no
    /// rastro e <b>nada é apagado</b>. Bloco falho não entra — por isso o chamador só chega aqui no
    /// caminho de sucesso.
    /// </summary>
    private async Task ReportMissingAsync(
        ImportRun run,
        ImportRunBlock block,
        List<ImportedOrderKey> importedBefore,
        HashSet<(string, DateOnly, OrderKind)> seen)
    {
        if (run.Mode == ImportRunMode.DryRun)
        {
            // Simulação não altera pedido nenhum, então "não encontrado" seria lido como se a próxima
            // gravação fosse apagar algo (FR-032). Nada é reportado.
            return;
        }

        foreach (var key in importedBefore)
        {
            if (seen.Contains((key.OrderNumber, key.OrderDate, key.Kind)))
            {
                continue;
            }

            await _runs.AddChangeAsync(ImportRunChange.Missing(
                run.Id, block.Id, key.OrderId, key.OrderNumber, key.OrderDate, key.Kind,
                key.Channel, key.Origin, _clock.UtcNow));

            block.Record(ImportChangeAction.Missing);
        }
    }

    /// <summary>
    /// Uma linha do rastro: só a chave natural, nunca CPF ou nome do cliente (FR-052). Em simulação o
    /// <paramref name="orderId"/> chega nulo — o link do relatório só existe onde o pedido existe.
    /// </summary>
    private async Task AddChangeAsync(
        ImportRun run,
        ImportRunBlock block,
        long? orderId,
        CollectedOrder order,
        ImportChangeAction action,
        List<string>? changedFields,
        string? errorMessage)
    {
        await _runs.AddChangeAsync(ImportRunChange.From(
            run.Id,
            block.Id,
            orderId,
            order.OrderNumber,
            order.OrderDate,
            order.Kind,
            order.Channel,
            order.Origin,
            action,
            _clock.UtcNow,
            changedFields,
            errorMessage));
    }

    /// <summary>
    /// Escreve o progresso. Os totais da execução são recalculados a partir dos blocos (FR-047), então
    /// esta chamada pode repetir-se sem duplicar contador.
    /// </summary>
    private async Task PersistProgressAsync(ImportRun run, ImportRunBlock block)
    {
        run.AccumulateBlock(block);

        await _runs.UpdateBlockAsync(block);
        await _runs.UpdateAsync(run);
    }

    private async Task FailBlockAsync(ImportRunBlock block, ImportRun run, string reason, string? detail)
    {
        block.Fail(reason, _clock.UtcNow);
        await PersistProgressAsync(run, block);

        if (detail is { Length: > 0 })
        {
            _logger.LogWarning("Bloco {BlockNo} falhou: {Reason} {Detail}", block.BlockNo, reason, detail);
        }
        else
        {
            _logger.LogWarning("Bloco {BlockNo} falhou: {Reason}", block.BlockNo, reason);
        }
    }

    /// <summary>Relê <c>cancel_requested</c>: a vendedora pode ter cancelado em outra aba ou no app.</summary>
    private async Task<bool> IsCancelRequestedAsync(ImportRun run, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return true;
        }

        var fresh = await _runs.GetByIdAsync(run.Id, run.UserId);

        return fresh?.CancelRequested == true;
    }

    /// <summary>Fecha com a FR-047a e persiste (research §18).</summary>
    private async Task FinishAsync(ImportRun run)
    {
        run.Finish(_clock.UtcNow);
        await _runs.UpdateAsync(run);

        // Finish marca os blocos que sobraram como notExecuted (FR-013) só em memória; UpdateAsync
        // grava os escalares da execução, então os blocos vão um a um — senão ficariam "pendentes"
        // para sempre no relatório de uma execução já encerrada.
        await PersistBlocksAsync(run);

        _logger.LogInformation(
            "Execução {RunId} encerrada como {Status}: {Inserted} inserido(s), {Updated} atualizado(s), {Unchanged} inalterado(s), {Missing} ausente(s), {Errors} erro, {Conflicts} conflito(s), {Customers} cliente(s) novo(s), {Images} foto(s), {ImagesFailed} foto(s) com erro.",
            run.Id, run.Status, run.InsertedCount, run.UpdatedCount, run.UnchangedCount,
            run.MissingCount, run.FailedCount, run.ConflictCount, run.CustomersCreatedCount,
            run.ImagesStoredCount, run.ImagesFailedCount);
    }

    /// <summary>
    /// Mensagem segura para <c>error_message</c>: nunca credencial, nunca CPF, sempre dentro da coluna.
    /// Os textos do contrato são preservados literalmente — a suíte de API os confere.
    /// </summary>
    private static string SafeMessage(Exception ex) => ex switch
    {
        MaisVendasSourceException source => source.Message,
        ImportBlockWriteException write => write.Message,
        _ => Truncate(ex.Message)
    };

    private static string Truncate(string value) =>
        value.Length <= ImportRun.ERROR_MESSAGE_MAX_LENGTH
            ? value
            : value[..ImportRun.ERROR_MESSAGE_MAX_LENGTH];

    /// <summary>Estado que atravessa a execução inteira e morre com ela.</summary>
    private sealed class RunState
    {
        /// <summary>Cache URL → imageId da execução (FR-040): mesmo SKU em três pedidos dá uma foto.</summary>
        public Dictionary<string, long> LinkedUrlToImageId { get; } = new(StringComparer.Ordinal);

        /// <summary>URLs distintas já consideradas na simulação (FR-032).</summary>
        public HashSet<string> PreviewCountedUrls { get; } = new(StringComparer.Ordinal);

        /// <summary>URLs que já estavam ligadas a um item antes da execução (simulação não as conta).</summary>
        public HashSet<string> AlreadyStoredUrls { get; } = new(StringComparer.Ordinal);

        /// <summary>CPF distintos cuja criação foi contada na simulação (FR-032).</summary>
        public HashSet<string> PreviewCreatedCpfHashes { get; } = new(StringComparer.Ordinal);
    }
}
