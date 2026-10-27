using Ambrosia.Domain.Common;
using Ambrosia.Domain.Exceptions;
using Ambrosia.Domain.Interfaces;
using Ambrosia.Domain.Models;
using Ambrosia.DTO.Common;
using Ambrosia.DTO.Enums;
using Ambrosia.DTO.Imports;
using Ambrosia.DTO.Settings;
using Ambrosia.Infra.Interfaces.AppServices;
using Ambrosia.Infra.Interfaces.Repository;
using FluentValidation;
using Microsoft.Extensions.Options;

namespace Ambrosia.Domain.Services;

/// <summary>
/// Casos de uso da tela de importação (feature 006): defaults, iniciar, consultar, listar e cancelar.
/// <para>
/// Nada aqui coleta nem grava pedido: o serviço é a porta de entrada HTTP-safe. <c>StartAsync</c>
/// <b>valida, grava a execução sem credencial, enfileira o job e devolve o resumo</b> — é o desenho que
/// permite o <c>202</c> em até 3 s (SC-006) e mantém a senha fora do banco, do log e da resposta
/// (FR-005/FR-006). O trabalho pesado é do <c>ImportRunWorker</c> via
/// <see cref="IImportExecutionService"/>.
/// </para>
/// </summary>
public class ImportRunService : IImportRunService
{
    private readonly IImportRunRepository<ImportRun, ImportRunBlock, ImportRunChange> _runs;
    private readonly IMaisVendasAccountRepository<MaisVendasAccount> _accounts;
    private readonly IImportRunQueue _queue;
    private readonly IValidator<ImportRunStartInfo> _startValidator;
    private readonly IValidator<ImportRunListQuery> _listValidator;
    private readonly IClock _clock;
    private readonly IMaisVendasCollectorAppService _collector;
    private readonly MaisVendasSettings _settings;

    public ImportRunService(
        IImportRunRepository<ImportRun, ImportRunBlock, ImportRunChange> runs,
        IMaisVendasAccountRepository<MaisVendasAccount> accounts,
        IImportRunQueue queue,
        IValidator<ImportRunStartInfo> startValidator,
        IValidator<ImportRunListQuery> listValidator,
        IClock clock,
        IMaisVendasCollectorAppService collector,
        IOptions<MaisVendasSettings> settings)
    {
        _runs = runs;
        _accounts = accounts;
        _queue = queue;
        _startValidator = startValidator;
        _listValidator = listValidator;
        _clock = clock;
        _collector = collector;
        _settings = settings.Value;
    }

    /// <inheritdoc />
    public async Task<ImportDefaultsInfo> GetDefaultsAsync(long userId)
    {
        var now = _clock.UtcNow;
        var account = await _accounts.GetOrCreateAsync(userId, now);

        return new ImportDefaultsInfo
        {
            // FR-002: o último dia já importado (null quando a vendedora nunca importou — a tela então
            // deixa a data inicial vazia e obrigatória).
            SuggestedStartDate = await _runs.GetSuggestedStartDateAsync(userId),
            SuggestedEndDate = SaoPauloCalendar.Today(_clock),
            BlockDays = _settings.BlockDays,
            MaxRangeDays = _settings.MaxRangeDays,

            // FR-008a: em bloqueio, o horário vai no corpo para a tela desabilitar o envio.
            LockedUntil = account.IsLocked(now) ? account.LockedUntil : null
        };
    }

    /// <inheritdoc />
    public async Task<ImportRunInfo> StartAsync(long userId, ImportRunStartInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);

        var result = await _startValidator.ValidateAsync(info);

        if (!result.IsValid)
        {
            throw new ValidationException(result.Errors);
        }

        var now = _clock.UtcNow;

        // O bloqueio por recusas é conferido aqui (e não no executor): a resposta é 429 com
        // Retry-After, e a execução nem nasce.
        var account = await _accounts.GetOrCreateAsync(userId, now);

        if (account.IsLocked(now))
        {
            throw new ImportLockedException(account.LockedUntil!.Value);
        }

        var mode = info.DryRun ? ImportRunMode.DryRun : ImportRunMode.Apply;

        // ImportRun.Create já planeja os blocos (FR-009), então blocksTotal é conhecido no 202.
        var run = ImportRun.Create(
            userId,
            mode,
            info.StartDate,
            info.EndDate,
            _settings.BlockDays,
            _collector.CollectorVersion,
            now);

        // DuplicateException (409, FR-016) vem do índice único parcial quando duas requisições da
        // mesma vendedora chegam juntas; a execução em curso não é tocada.
        var saved = await _runs.InsertAsync(run);

        // As credenciais só existem neste objeto em memória (research §4): nada daqui vai para o banco,
        // para o log ou para a resposta.
        _queue.Enqueue(new ImportJob
        {
            RunId = saved.Id,
            UserId = userId,
            Mode = mode,
            StartDate = info.StartDate,
            EndDate = info.EndDate,
            Login = info.Login,
            Password = info.Password
        });

        return MapToInfo(saved);
    }

    /// <inheritdoc />
    public async Task<ImportRunDetailInfo?> GetCurrentAsync(long userId)
    {
        var run = await _runs.GetCurrentAsync(userId);

        return run is null ? null : await BuildDetailAsync(run, userId);
    }

    /// <inheritdoc />
    public async Task<ImportRunDetailInfo?> GetByIdAsync(long userId, long runId)
    {
        var run = await _runs.GetByIdAsync(runId, userId);

        // Não existe e é de outra vendedora respondem idêntico (FR-053, §VI): o 404 não revela nada.
        return run is null ? null : await BuildDetailAsync(run, userId);
    }

    /// <inheritdoc />
    public async Task<PagedResult<ImportRunInfo>> ListAsync(long userId, ImportRunListQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var result = await _listValidator.ValidateAsync(query);

        if (!result.IsValid)
        {
            throw new ValidationException(result.Errors);
        }

        var (items, totalCount) = await _runs.ListAsync(userId, query.Page, query.PageSize);

        return new PagedResult<ImportRunInfo>
        {
            Items = items.Select(MapToInfo).ToList(),
            Page = query.Page,
            PageSize = query.PageSize,
            TotalCount = totalCount
        };
    }

    /// <inheritdoc />
    public async Task<ImportRunDetailInfo> CancelAsync(long userId, long runId)
    {
        var run = await _runs.GetByIdAsync(runId, userId)
            ?? throw new NotFoundException("Execução de importação não encontrada.");

        if (run.Status is ImportRunStatus.Queued)
        {
            // Ainda não abriu navegador nenhum: encerra de imediato, sem esperar bloco (T091
            // Cancel_Queued_CancelsImmediately).
            run.RequestCancel();
            run.Finish(_clock.UtcNow);

            await _runs.UpdateAsync(run);

            // Os blocos viraram notExecuted só em memória (Finish): UpdateAsync grava os escalares.
            foreach (var block in run.Blocks)
            {
                await _runs.UpdateBlockAsync(block);
            }

            // Relê: UpdateAsync grava os escalares em outra instância rastreada e devolve essa
            // instância, que não traz os blocos carregados — e o relatório de uma execução cancelada
            // precisa mostrar quais blocos ficaram notExecuted (contrato imports-api.md).
            var reloaded = await _runs.GetByIdAsync(runId, userId) ?? run;

            return await BuildDetailAsync(reloaded, userId);
        }

        if (run.Status is ImportRunStatus.Running)
        {
            // O bloco em curso termina inteiro — é o que permite retomar de onde parou sem perder o
            // que entrou — e o executor fecha como cancelled (FR-019).
            await _runs.RequestCancelAsync(runId, userId);

            run.RequestCancel();
        }
        else
        {
            throw new ImportRunNotCancellableException();
        }

        return await BuildDetailAsync(run, userId);
    }

    // ------------------------------------------------------------------ mapeamento

    /// <summary>
    /// Detalhe do relatório: resumo + blocos + rastro + agrupamento + comparação com a execução
    /// anterior do mesmo período (FR-048/FR-049/FR-051).
    /// </summary>
    private async Task<ImportRunDetailInfo> BuildDetailAsync(ImportRun run, long userId)
    {
        var detail = new ImportRunDetailInfo
        {
            Id = run.Id,
            Mode = run.Mode,
            Status = run.Status,
            StartDate = run.StartDate,
            EndDate = run.EndDate,
            BlockDays = run.BlockDays,
            BlocksTotal = run.BlocksTotal,
            BlocksFinished = run.BlocksFinished,
            CurrentBlockNo = run.CurrentBlockNo,
            CancelRequested = run.CancelRequested,
            RequestedAt = run.RequestedAt,
            StartedAt = run.StartedAt,
            FinishedAt = run.FinishedAt,
            DurationSeconds = run.DurationSeconds,
            Counts = MapCounts(run),
            ErrorMessage = run.ErrorMessage,
            Blocks = run.Blocks.OrderBy(b => b.BlockNo).Select(MapBlock).ToList(),
            Changes = (await _runs.ListChangesAsync(run.Id, userId)).Select(MapChange).ToList(),
            Breakdown = await _runs.GetBreakdownAsync(run.Id, userId)
        };

        // FR-051, research §18: a comparação é com a última execução concluída/parcial em gravação do
        // MESMO período. null/false quando não há o que comparar.
        var previous = await _runs.GetPreviousRunOrdersCountAsync(userId, run.Id, run.StartDate, run.EndDate);

        detail.PreviousRunOrdersCount = previous;
        detail.CountDropWarning = previous is { } count && count > 0 && run.OrdersSeen < count;

        return detail;
    }

    private static ImportRunInfo MapToInfo(ImportRun run) => new()
    {
        Id = run.Id,
        Mode = run.Mode,
        Status = run.Status,
        StartDate = run.StartDate,
        EndDate = run.EndDate,
        BlockDays = run.BlockDays,
        BlocksTotal = run.BlocksTotal,
        BlocksFinished = run.BlocksFinished,
        CurrentBlockNo = run.CurrentBlockNo,
        CancelRequested = run.CancelRequested,
        RequestedAt = run.RequestedAt,
        StartedAt = run.StartedAt,
        FinishedAt = run.FinishedAt,
        DurationSeconds = run.DurationSeconds,
        Counts = MapCounts(run),
        ErrorMessage = run.ErrorMessage
    };

    /// <summary>
    /// Nove contagens do resumo. Nunca derivadas de <c>changes</c> na leitura: o banco já guarda o
    /// total acumulado, e o relatório tem de sair igual ao que a execução registrou pedido a pedido.
    /// </summary>
    private static ImportRunCountsInfo MapCounts(ImportRun run) => new()
    {
        Inserted = run.InsertedCount,
        Updated = run.UpdatedCount,
        Unchanged = run.UnchangedCount,
        Missing = run.MissingCount,
        Failed = run.FailedCount,
        Conflicts = run.ConflictCount,
        CustomersCreated = run.CustomersCreatedCount,
        ImagesStored = run.ImagesStoredCount,
        ImagesFailed = run.ImagesFailedCount
    };

    private static ImportRunBlockInfo MapBlock(ImportRunBlock block) => new()
    {
        BlockNo = block.BlockNo,
        StartDate = block.StartDate,
        EndDate = block.EndDate,
        Status = block.Status,
        Counts = new ImportRunCountsInfo
        {
            Inserted = block.InsertedCount,
            Updated = block.UpdatedCount,
            Unchanged = block.UnchangedCount,
            Missing = block.MissingCount,
            Failed = block.FailedCount,
            Conflicts = block.ConflictCount,
            CustomersCreated = block.CustomersCreatedCount,
            ImagesStored = block.ImagesStoredCount,
            ImagesFailed = block.ImagesFailedCount
        },
        ErrorMessage = block.ErrorMessage,
        StartedAt = block.StartedAt,
        FinishedAt = block.FinishedAt
    };

    private static ImportRunChangeInfo MapChange(ImportRunChange change) => new()
    {
        OrderId = change.OrderId,
        OrderNumber = change.OrderNumber,
        OrderDate = change.OrderDate,
        Kind = change.Kind,
        Channel = change.Channel,
        Origin = change.Origin,
        Action = change.Action,
        ChangedFields = change.ChangedFieldsList(),
        ErrorMessage = change.ErrorMessage
    };
}
