using Ambrosio.Domain.Common;
using Ambrosio.Domain.Exceptions;
using Ambrosio.Domain.Interfaces;
using Ambrosio.Domain.Models;
using Ambrosio.DTO.Common;
using Ambrosio.DTO.Enums;
using Ambrosio.DTO.Imports;
using Ambrosio.DTO.Settings;
using Ambrosio.Infra.Interfaces.AppServices;
using Ambrosio.Infra.Interfaces.Repository;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Options;

namespace Ambrosio.Domain.Services;

/// <summary>
/// Casos de uso da tela de importação (feature 006): defaults, iniciar, consultar, listar e cancelar.
/// <para>
/// Nada aqui coleta nem grava pedido: o serviço é a porta de entrada HTTP-safe. <c>StartAsync</c>
/// <b>valida, confere as credenciais guardadas, grava a execução, e é só</b> — o <c>ImportJob</c> não
/// existe mais como transporte de credencial (feature 008, FR-003), e quem executa é o worker da API,
/// que reivindica a linha <c>queued</c> no banco (research §4 da 006 reescrito pela R7/R9 da 008). É o
/// desenho que mantém o <c>202</c> em até 3 s (SC-006) e permite que o plugin do ChatGPT inicie uma
/// importação sem ter navegador nenhum no processo dele.
/// </para>
/// <para>
/// <b>Sem credencial no corpo desde a feature 008</b>: <c>POST /runs</c> pede período e modo; a senha
/// mora cifrada em <c>mais_vendas_accounts</c> e é decifrada pelo executor no instante do login na
/// origem. A checagem de existência acontece aqui, na entrada, para responder <c>400</c> com a mensagem
/// única em vez de criar uma execução fadada a falhar (FR-002 da 008).
/// </para>
/// </summary>
public class ImportRunService : IImportRunService
{
    /// <summary>
    /// Motivo de <c>updateBase.available = false</c> quando há pedidos importados, mas o intervalo até
    /// hoje passa do limite (FR-015). O número vem da configuração, como na recusa de período — tela,
    /// servidor e plugin têm de repetir a mesma frase (SC-005).
    /// </summary>
    private const string UPDATE_BASE_TOO_LONG =
        "A última importação foi há mais de {0} meses. Importe em partes de até {0} meses, escolhendo as datas.";

    /// <summary>Motivo de <c>updateBase.available = false</c> quando não há nada importado (FR-015).</summary>
    private const string UPDATE_BASE_NOTHING = "Ainda não há pedidos importados para atualizar.";

    private readonly IImportRunRepository<ImportRun, ImportRunBlock, ImportRunChange> _runs;
    private readonly IMaisVendasAccountRepository<MaisVendasAccount> _accounts;
    private readonly IMaisVendasCredentialService _credentials;
    private readonly IValidator<ImportRunStartInfo> _startValidator;
    private readonly IValidator<ImportRunListQuery> _listValidator;
    private readonly IClock _clock;
    private readonly IMaisVendasCollectorAppService _collector;
    private readonly MaisVendasSettings _settings;

    public ImportRunService(
        IImportRunRepository<ImportRun, ImportRunBlock, ImportRunChange> runs,
        IMaisVendasAccountRepository<MaisVendasAccount> accounts,
        IMaisVendasCredentialService credentials,
        IValidator<ImportRunStartInfo> startValidator,
        IValidator<ImportRunListQuery> listValidator,
        IClock clock,
        IMaisVendasCollectorAppService collector,
        IOptions<MaisVendasSettings> settings)
    {
        _runs = runs;
        _accounts = accounts;
        _credentials = credentials;
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
            MaxRangeMonths = _settings.MaxRangeMonths,

            // FR-004/FR-009 da 008: saem das colunas, sem decifrar nada. É o que liga o botão da tela
            // e o que o plugin lê para orientar a vendedora sem expor credencial.
            HasCredentials = account.HasCredentials,
            CredentialsStatus = account.HasCredentials
                ? account.CredentialsStatus
                : MaisVendasCredentialsStatus.None,

            // FR-008a: em bloqueio, o horário vai no corpo para a tela desabilitar o envio.
            LockedUntil = account.IsLocked(now) ? account.LockedUntil : null,

            // FR-011/FR-013: o período que o "Atualizar base" usaria agora, já calculado.
            UpdateBase = await ComputeUpdateBaseAsync(userId)
        };
    }

    /// <inheritdoc />
    public async Task<ImportRunInfo> StartAsync(long userId, ImportRunStartInfo info, ImportRunStartedVia startedVia)
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

        // FR-002 da 008: sem credenciais cadastradas não há o que coletar, e a recusa é uma frase só —
        // a mesma da tela, do plugin e do importação avulsa (contrato). Criar a execução para ela falhar
        // depois no worker deixaria a vendedora olhando um "falhou" sem causa visível.
        if (!account.HasCredentials)
        {
            throw new ValidationException([
                new ValidationFailure("login", IMaisVendasCredentialService.NOT_SAVED_MESSAGE)]);
        }

        var mode = info.DryRun ? ImportRunMode.DryRun : ImportRunMode.Apply;

        var saved = await CreateRunAsync(
            userId,
            mode,
            info.StartDate,
            info.EndDate,
            ImportRunKind.Period,
            startedVia,
            now);

        return MapToInfo(saved);
    }

    /// <inheritdoc />
    public async Task<ImportRunInfo> UpdateBaseAsync(long userId, bool dryRun, ImportRunStartedVia startedVia)
    {
        var now = _clock.UtcNow;
        var account = await _accounts.GetOrCreateAsync(userId, now);

        if (account.IsLocked(now))
        {
            throw new ImportLockedException(account.LockedUntil!.Value);
        }

        if (!account.HasCredentials)
        {
            throw new ValidationException([
                new ValidationFailure("login", IMaisVendasCredentialService.NOT_SAVED_MESSAGE)]);
        }

        // FR-013: as datas NÃO vêm de quem pediu. Este cálculo é a única fonte do período, e a recusa
        // devolve o mesmo motivo que o `defaults` já mostrava na tela.
        var updateBase = await ComputeUpdateBaseAsync(userId);

        if (!updateBase.Available)
        {
            throw new ValidationException([
                new ValidationFailure("startDate", updateBase.Reason ?? UPDATE_BASE_NOTHING)]);
        }

        var saved = await CreateRunAsync(
            userId,
            dryRun ? ImportRunMode.DryRun : ImportRunMode.Apply,
            updateBase.StartDate!.Value,
            updateBase.EndDate!.Value,
            ImportRunKind.UpdateBase,
            startedVia,
            now);

        return MapToInfo(saved);
    }

    /// <summary>
    /// Calcula o "Atualizar base" (feature 008, FR-011/FR-012/FR-015, research R5): do último pedido
    /// importado, inclusive, até hoje no fuso de São Paulo.
    /// <para>
    /// Estar disponível exige três coisas: ter importado algo em modo gravação que terminou dentro do
    /// corte (<c>GetLastImportedBySellerSinceAsync</c>, com o corte por <see cref="SaoPauloCalendar.AddMonthsFloor"/>
    /// — 26/07 a 25/10 entra, 26/07 a 26/10 não), ter a data do último pedido importado, e o período
    /// calculado caber no limite. Simulação não conta: ela não atualizou base nenhuma.
    /// </para>
    /// <para>
    /// <c>startDate</c> é a data do <b>último pedido importado</b> e não o <c>start_date</c> da última
    /// execução: uma execução cobre um período escolhido, e dentro dele podem não ter existido pedidos
    /// — a base real termina no último pedido que existe.
    /// </para>
    /// </summary>
    private async Task<ImportUpdateBaseInfo> ComputeUpdateBaseAsync(long userId)
    {
        var today = SaoPauloCalendar.Today(_clock);
        var floor = SaoPauloCalendar.AddMonthsFloor(today, _settings.MaxRangeMonths);

        var lastFinishedAt = await _accounts.GetLastImportedBySellerSinceAsync(userId, floor.ToDateTime(TimeOnly.MinValue));

        if (lastFinishedAt is null)
        {
            // Dois motivos diferentes, um só teste: sem nenhum pedido importado x importação antiga.
            var suggested = await _runs.GetSuggestedStartDateAsync(userId);

            return new ImportUpdateBaseInfo
            {
                Available = false,
                Reason = suggested is null ? UPDATE_BASE_NOTHING : FormatTooLong()
            };
        }

        var startDate = await _runs.GetSuggestedStartDateAsync(userId);

        if (startDate is null)
        {
            return new ImportUpdateBaseInfo { Available = false, Reason = UPDATE_BASE_NOTHING };
        }

        if (startDate < floor)
        {
            return new ImportUpdateBaseInfo { Available = false, Reason = FormatTooLong() };
        }

        return new ImportUpdateBaseInfo
        {
            Available = true,
            StartDate = startDate,
            EndDate = today
        };

        string FormatTooLong() => string.Format(UPDATE_BASE_TOO_LONG, _settings.MaxRangeMonths);
    }

    /// <summary>
    /// Cria a execução <c>queued</c> e a devolve persistida (o caminho comum do período escolhido e do
    /// "Atualizar base": mudam o <see cref="ImportRunKind"/>, não a mecânica).
    /// <para>
    /// Nada é enfileirado em memória (research R9 da 008): a linha <c>queued</c> no banco É a fila, e o
    /// worker da API — único processo com navegador — a reivindica. Assim o host do MCP consegue iniciar
    /// uma importação sem coletar nada, e a recuperação de um reinício (FR-020) passa a valer também
    /// para o que veio da conversa.
    /// </para>
    /// </summary>
    private async Task<ImportRun> CreateRunAsync(
        long userId,
        ImportRunMode mode,
        DateOnly startDate,
        DateOnly endDate,
        ImportRunKind kind,
        ImportRunStartedVia startedVia,
        DateTime now)
    {
        // ImportRun.Create já planeja os blocos (FR-009), então blocksTotal é conhecido no 202.
        var run = ImportRun.Create(
            userId,
            mode,
            startDate,
            endDate,
            _settings.BlockDays,
            _collector.CollectorVersion,
            now,
            startedVia,
            kind);

        // DuplicateException (409, FR-016) vem do índice único parcial quando duas requisições da
        // mesma vendedora chegam juntas; a execução em curso não é tocada.
        return await _runs.InsertAsync(run);
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
            StartedVia = run.StartedVia,
            Kind = run.Kind,
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
        StartedVia = run.StartedVia,
        Kind = run.Kind,
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
