using Ambrosia.DTO.Enums;

namespace Ambrosia.Domain.Models;

/// <summary>
/// Fatia contígua do período de uma importação (feature 006, FR-009): a unidade de progresso da tela e
/// de retomada da vendedora. As nove contagens próprias (data-model §2) é que permitem ao relatório
/// dizer "o bloco 3 falhou com 14 pedidos gravados" (US3 cenário 1) — sem elas, repetir um bloco
/// seria às cegas.
/// <para>
/// Não há FK em cascata nem exclusão: <c>import_run_blocks</c> é histórico do que aconteceu (constituição §V).
/// </para>
/// </summary>
public class ImportRunBlock
{
    public long Id { get; set; }

    public long ImportRunId { get; set; }

    /// <summary>Posição no período, 1-based (FR-009). Único por execução (<c>ux_import_run_blocks_run_no</c>).</summary>
    public int BlockNo { get; set; }

    public DateOnly StartDate { get; set; }

    public DateOnly EndDate { get; set; }

    public ImportBlockStatus Status { get; set; } = ImportBlockStatus.Pending;

    public int InsertedCount { get; set; }

    public int UpdatedCount { get; set; }

    public int UnchangedCount { get; set; }

    public int MissingCount { get; set; }

    public int FailedCount { get; set; }

    public int ConflictCount { get; set; }

    public int CustomersCreatedCount { get; set; }

    public int ImagesStoredCount { get; set; }

    public int ImagesFailedCount { get; set; }

    /// <summary>Motivo quando <c>failed</c>; truncado e nunca com login, senha ou CPF (FR-052).</summary>
    public string? ErrorMessage { get; set; }

    public DateTime? StartedAt { get; set; }

    public DateTime? FinishedAt { get; set; }

    public ImportRun? ImportRun { get; set; }

    /// <summary>Limite da coluna <c>error_message</c> (data-model §2).</summary>
    public const int ERROR_MESSAGE_MAX_LENGTH = 500;

    /// <summary>Um bloco ainda não começado, planejado por <c>ImportBlockPlanner</c>.</summary>
    public static ImportRunBlock CreatePending(int blockNo, DateOnly startDate, DateOnly endDate) => new()
    {
        BlockNo = blockNo,
        StartDate = startDate,
        EndDate = endDate,
        Status = ImportBlockStatus.Pending
    };

    /// <summary>
    /// Coleta começando. Exige <c>pending</c>: um bloco já terminado não volta a rodar — a retomada
    /// cria uma <b>execução nova</b> (a idempotência cuida do resto), nunca um reuso deste registro.
    /// </summary>
    /// <exception cref="InvalidOperationException">Bloco fora de <c>pending</c>.</exception>
    public void Start(DateTime now)
    {
        if (Status != ImportBlockStatus.Pending)
        {
            throw new InvalidOperationException(
                $"O bloco {BlockNo} está em status {Status}; só um bloco pendente pode começar.");
        }

        Status = ImportBlockStatus.Running;
        StartedAt = now;
    }

    /// <summary>Bloco coletado, validado e gravado.</summary>
    public void Succeed(DateTime now)
    {
        Status = ImportBlockStatus.Succeeded;
        ErrorMessage = null;
        FinishedAt = now;
    }

    /// <summary>
    /// Falha do bloco: a causa vai na mensagem (truncada, sem dado pessoal) e a execução para (FR-013).
    /// As contagens feitas até aqui ficam — é "quantos pedidos daquele bloco chegaram a entrar".
    /// </summary>
    public void Fail(string reason, DateTime now)
    {
        Status = ImportBlockStatus.Failed;
        ErrorMessage = Truncate(reason);
        FinishedAt = now;
    }

    /// <summary>A execução parou antes deste bloco (falha anterior, cancelamento ou reinício do servidor).</summary>
    public void MarkNotExecuted()
    {
        if (Status is ImportBlockStatus.Pending or ImportBlockStatus.Running)
        {
            Status = ImportBlockStatus.NotExecuted;
        }
    }

    /// <summary>Incrementa a contagem correspondente à decisão tomada para um pedido (FR-047).</summary>
    /// <exception cref="ArgumentOutOfRangeException">Ação sem contagem própria.</exception>
    public void Record(ImportChangeAction action)
    {
        switch (action)
        {
            case ImportChangeAction.Inserted:
                InsertedCount++;
                break;
            case ImportChangeAction.Updated:
                UpdatedCount++;
                break;
            case ImportChangeAction.Unchanged:
                UnchangedCount++;
                break;
            case ImportChangeAction.Missing:
                MissingCount++;
                break;
            case ImportChangeAction.Conflict:
                ConflictCount++;
                break;
            case ImportChangeAction.Error:
                FailedCount++;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(action), action, "Ação sem contagem.");
        }
    }

    /// <summary>Conta uma foto gravada no storage (FR-037).</summary>
    public void RecordImageStored() => ImagesStoredCount++;

    /// <summary>Conta uma foto que falhou ou foi recusada; o pedido continua gravado (FR-039).</summary>
    public void RecordImageFailed() => ImagesFailedCount++;

    /// <summary>Conta um cliente criado sob demanda (FR-034).</summary>
    public void RecordCustomerCreated() => CustomersCreatedCount++;

    /// <summary>Se o bloco terminou (qualquer status final).</summary>
    public bool IsFinished => Status is ImportBlockStatus.Succeeded
        or ImportBlockStatus.Failed
        or ImportBlockStatus.NotExecuted;

    /// <summary>Pedidos que chegaram a ser gravados por este bloco (US3 cenário 1).</summary>
    public int OrdersWritten => InsertedCount + UpdatedCount + UnchangedCount;

    private static string? Truncate(string? value) =>
        string.IsNullOrEmpty(value) || value.Length <= ERROR_MESSAGE_MAX_LENGTH
            ? value
            : value[..ERROR_MESSAGE_MAX_LENGTH];
}
