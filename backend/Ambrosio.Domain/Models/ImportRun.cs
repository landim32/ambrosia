using Ambrosio.Domain.Common;
using Ambrosio.DTO.Enums;

namespace Ambrosio.Domain.Models;

/// <summary>
/// Uma execução de importação do +Vendas (feature 006, tabela <c>import_runs</c>): o período pedido, o
/// modo, o progresso bloco a bloco, as nove contagens e o motivo do fim.
/// <para>
/// <b>Nada aqui guarda credencial</b> (FR-003 da 008): desde a feature 008 o login e a senha vivem só
/// cifrados em <see cref="MaisVendasAccount"/> e o <c>ImportJob</c> não os carrega — o executor os
/// decifra no instante do login no +Vendas (research R3). O que fica é o rastro operacional: datas,
/// origem, tipo, status, blocos, contagens e a mensagem final.
/// </para>
/// <para>
/// Uma execução ativa por vendedora é garantida pelo índice único parcial
/// <c>ux_import_runs_user_active (user_id) WHERE status IN (1, 2)</c> (FR-016); a corrida perde com
/// <c>23505</c> e vira <c>409</c> no repositório.
/// </para>
/// </summary>
public class ImportRun
{
    /// <summary>Limite da coluna <c>error_message</c> (data-model §2).</summary>
    public const int ERROR_MESSAGE_MAX_LENGTH = 500;

    public long Id { get; set; }

    /// <summary>Dono, do claim <c>sub</c>. Nunca vem de DTO de entrada (constituição §VI).</summary>
    public long UserId { get; set; }

    public ImportRunMode Mode { get; set; }

    /// <summary>
    /// De onde veio o pedido: tela ou plugin do ChatGPT (feature 008, research R8). Quem chama o
    /// <c>IImportRunService</c> informa — o <c>sub</c> nunca vem do corpo, e a origem também não.
    /// </summary>
    public ImportRunStartedVia StartedVia { get; set; } = ImportRunStartedVia.Screen;

    /// <summary>
    /// O que foi pedido: período escolhido ou "Atualizar base" (feature 008, research R5/R8). No
    /// "Atualizar base" as datas são calculadas no servidor, não enviadas por quem pediu.
    /// </summary>
    public ImportRunKind Kind { get; set; } = ImportRunKind.Period;

    public ImportRunStatus Status { get; set; } = ImportRunStatus.Queued;

    public DateOnly StartDate { get; set; }

    public DateOnly EndDate { get; set; }

    /// <summary>Tamanho do bloco em dias vigente nesta execução (mudar a configuração não reescreve histórico).</summary>
    public int BlockDays { get; set; }

    public int BlocksTotal { get; set; }

    /// <summary>Cancelamento pedido pela vendedora; atende ao fim do bloco em curso (FR-019).</summary>
    public bool CancelRequested { get; set; }

    public DateTime RequestedAt { get; set; }

    public DateTime? StartedAt { get; set; }

    public DateTime? FinishedAt { get; set; }

    public int InsertedCount { get; set; }

    public int UpdatedCount { get; set; }

    public int UnchangedCount { get; set; }

    public int MissingCount { get; set; }

    public int FailedCount { get; set; }

    public int ConflictCount { get; set; }

    public int CustomersCreatedCount { get; set; }

    public int ImagesStoredCount { get; set; }

    public int ImagesFailedCount { get; set; }

    /// <summary>Motivo em <c>failed</c>/<c>cancelled</c>; nunca login, senha ou CPF (FR-052).</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Qual coletor produziu os números: <c>playwright-1.0</c>, <c>fake-1.0</c> (data-model §2).</summary>
    public string CollectorVersion { get; set; } = string.Empty;

    public List<ImportRunBlock> Blocks { get; set; } = [];

    public User? User { get; set; }

    /// <summary>
    /// Cria a execução <c>queued</c> com todos os blocos já planejados como <c>pending</c> (FR-009):
    /// a tela sabe "5 blocos" antes de o worker começar, e o histórico de uma execução interrompida
    /// mostra quais blocos não rodaram (FR-020).
    /// </summary>
    /// <param name="collectorVersion">Versão do coletor escolhido pela configuração.</param>
    /// <param name="now">Instante UTC de <see cref="Interfaces.IClock"/>.</param>
    /// <param name="startedVia">Tela ou plugin (feature 008, research R8).</param>
    /// <param name="kind">Período escolhido ou "Atualizar base" (feature 008, research R5).</param>
    public static ImportRun Create(
        long userId,
        ImportRunMode mode,
        DateOnly startDate,
        DateOnly endDate,
        int blockDays,
        string collectorVersion,
        DateTime now,
        ImportRunStartedVia startedVia = ImportRunStartedVia.Screen,
        ImportRunKind kind = ImportRunKind.Period)
    {
        var plan = ImportBlockPlanner.Split(startDate, endDate, blockDays);

        var run = new ImportRun
        {
            UserId = userId,
            Mode = mode,
            StartedVia = startedVia,
            Kind = kind,
            Status = ImportRunStatus.Queued,
            StartDate = startDate,
            EndDate = endDate,
            BlockDays = blockDays,
            BlocksTotal = plan.Count,
            CancelRequested = false,
            RequestedAt = now,
            CollectorVersion = collectorVersion
        };

        run.Blocks = plan
            .Select(b => ImportRunBlock.CreatePending(b.BlockNo, b.StartDate, b.EndDate))
            .ToList();

        return run;
    }

    /// <summary>Saiu da fila: ganhou vaga no semáforo e começou (FR-017).</summary>
    public void Start(DateTime now)
    {
        Status = ImportRunStatus.Running;
        StartedAt ??= now;
    }

    /// <summary>
    /// A vendedora pediu o cancelamento. Válido só em <c>queued</c>/<c>running</c>: execução terminada
    /// não é cancelável, e é isso que o controller traduz em <c>409</c> (FR-019).
    /// </summary>
    /// <returns><c>true</c> quando havia mesmo o que cancelar.</returns>
    public bool RequestCancel()
    {
        if (Status is not (ImportRunStatus.Queued or ImportRunStatus.Running))
        {
            return false;
        }

        CancelRequested = true;

        return true;
    }

    /// <summary>
    /// Refaz os totais da execução como a soma das contagens dos blocos (FR-047).
    /// <para>
    /// É uma <b>recontagem</b> e não um acréscimo de propósito: o progresso é gravado depois de cada
    /// pedido, então a mesma chamada acontece muitas vezes por bloco, e somar de novo duplicaria tudo.
    /// Recalcular a partir dos blocos mantém o resumo idêntico ao detalhe em qualquer instante — e
    /// sobrevive a uma releitura do estado pelo worker.
    /// </para>
    /// </summary>
    public void AccumulateBlock(ImportRunBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);

        // Os blocos carregados são a fonte única; se a execução foi reidratada sem eles (o caminho
        // UpdateAsync grava só os escalares), as contagens do bloco que terminou são o que se conhece.
        if (Blocks.Count == 0)
        {
            InsertedCount += block.InsertedCount;
            UpdatedCount += block.UpdatedCount;
            UnchangedCount += block.UnchangedCount;
            MissingCount += block.MissingCount;
            FailedCount += block.FailedCount;
            ConflictCount += block.ConflictCount;
            CustomersCreatedCount += block.CustomersCreatedCount;
            ImagesStoredCount += block.ImagesStoredCount;
            ImagesFailedCount += block.ImagesFailedCount;

            return;
        }

        InsertedCount = Blocks.Sum(b => b.InsertedCount);
        UpdatedCount = Blocks.Sum(b => b.UpdatedCount);
        UnchangedCount = Blocks.Sum(b => b.UnchangedCount);
        MissingCount = Blocks.Sum(b => b.MissingCount);
        FailedCount = Blocks.Sum(b => b.FailedCount);
        ConflictCount = Blocks.Sum(b => b.ConflictCount);
        CustomersCreatedCount = Blocks.Sum(b => b.CustomersCreatedCount);
        ImagesStoredCount = Blocks.Sum(b => b.ImagesStoredCount);
        ImagesFailedCount = Blocks.Sum(b => b.ImagesFailedCount);
    }

    /// <summary>
    /// Falha <b>antes</b> de qualquer bloco rodar — credenciais recusadas, conta diferente da
    /// vinculada, etapa de login não suportada (FR-007/FR-008b/FR-046). Todos os blocos viram
    /// <c>notExecuted</c> e as contagens ficam zeradas: nada foi gravado (contrato imports-api.md).
    /// </summary>
    public void FailBeforeBlocks(string reason, DateTime now)
    {
        Status = ImportRunStatus.Failed;
        ErrorMessage = Truncate(reason);
        StartedAt ??= now;
        FinishedAt = now;

        foreach (var block in Blocks)
        {
            block.MarkNotExecuted();
        }
    }

    /// <summary>
    /// Fecha a execução aplicando a FR-047a exatamente como o research §18 define, nesta ordem:
    /// <list type="number">
    /// <item>status já final (o caso é a falha <b>antes</b> dos blocos, registrada por
    /// <see cref="FailBeforeBlocks"/>) — conserva o que já foi decidido;</item>
    /// <item><c>cancelled</c> — a vendedora pediu o cancelamento;</item>
    /// <item><c>failed</c> — algum bloco falhou;</item>
    /// <item><c>partial</c> — todos os blocos terminaram, mas houve pedido com erro, conflito com
    /// pedido manual ou imagem com erro;</item>
    /// <item><c>succeeded</c> — caso contrário.</item>
    /// </list>
    /// Blocos <c>pending</c> que sobraram viram <c>notExecuted</c> (FR-013).
    /// </summary>
    public void Finish(DateTime now)
    {
        foreach (var block in Blocks)
        {
            block.MarkNotExecuted();
        }

        StartedAt ??= now;

        // Um status já final não é reclassificado: é como "falha antes dos blocos" sobrevive a
        // qualquer fechamento posterior, sem precisar de uma coluna de estado extra.
        if (Status is not (ImportRunStatus.Queued or ImportRunStatus.Running))
        {
            FinishedAt ??= now;

            return;
        }

        if (CancelRequested)
        {
            Status = ImportRunStatus.Cancelled;
        }
        else if (Blocks.Any(b => b.Status == ImportBlockStatus.Failed))
        {
            Status = ImportRunStatus.Failed;
        }
        else if (FailedCount + ConflictCount + ImagesFailedCount > 0)
        {
            Status = ImportRunStatus.Partial;
        }
        else
        {
            Status = ImportRunStatus.Succeeded;
        }

        FinishedAt = now;
    }

    /// <summary>Execução em <c>queued</c> ou <c>running</c> — conta no índice único parcial (FR-016).</summary>
    public bool IsActive => Status is ImportRunStatus.Queued or ImportRunStatus.Running;

    /// <summary>Blocos já terminados, para "2 de 5" na tela (FR-015).</summary>
    public int BlocksFinished => Blocks.Count(b => b.IsFinished);

    /// <summary>Número do bloco em curso, ou <c>null</c> quando não há.</summary>
    public int? CurrentBlockNo => Blocks.FirstOrDefault(b => b.Status == ImportBlockStatus.Running)?.BlockNo;

    /// <summary>Próximo bloco a rodar (<c>pending</c> de menor número); <c>null</c> quando não sobra.</summary>
    public ImportRunBlock? NextPendingBlock() =>
        Blocks.Where(b => b.Status == ImportBlockStatus.Pending).OrderBy(b => b.BlockNo).FirstOrDefault();

    /// <summary>Duração em segundos entre o início e o fim; <c>null</c> enquanto não terminou.</summary>
    public long? DurationSeconds =>
        StartedAt is { } started && FinishedAt is { } finished
            ? (long)Math.Max(0, Math.Round((finished - started).TotalSeconds))
            : null;

    /// <summary>Total de pedidos vistos pela origem nesta execução (base do aviso de queda, FR-051).</summary>
    public int OrdersSeen => InsertedCount + UpdatedCount + UnchangedCount;

    private static string? Truncate(string? value) =>
        string.IsNullOrEmpty(value) || value.Length <= ERROR_MESSAGE_MAX_LENGTH
            ? value
            : value[..ERROR_MESSAGE_MAX_LENGTH];
}
