using Ambrosio.DTO.Enums;

namespace Ambrosio.Domain.Models;

/// <summary>
/// O rastro de <b>um</b> pedido numa importação (feature 006, tabela <c>import_run_changes</c>,
/// FR-048): a linha que alimenta a tabela do relatório e o agrupamento canal × origem × tipo × ação.
/// <para>
/// Guarda apenas a <b>chave natural</b> do pedido — número, data e tipo — mais o resultado da decisão.
/// <b>Nunca</b> o CPF nem o nome do cliente (FR-052): o rastro é lido por qualquer listagem do
/// histórico, e nome/CPF de um comprador não têm por que atravessar uma tela de importação.
/// </para>
/// </summary>
public class ImportRunChange
{
    /// <summary>Limite da coluna <c>changed_fields</c> (data-model §2).</summary>
    public const int CHANGED_FIELDS_MAX_LENGTH = 500;

    /// <summary>Limite da coluna <c>error_message</c>.</summary>
    public const int ERROR_MESSAGE_MAX_LENGTH = 500;

    public long Id { get; set; }

    public long ImportRunId { get; set; }

    public long ImportRunBlockId { get; set; }

    /// <summary>
    /// Pedido correspondente, quando existe e quando houve gravação. Fica <c>null</c> em simulação
    /// (nada foi gravado), em <c>error</c> e em <c>missing</c> quando o pedido sumiu da base — é o que
    /// faz o link <c>/pedidos/{id}</c> aparecer só onde ele tem destino (FR-049).
    /// </summary>
    public long? OrderId { get; set; }

    public string OrderNumber { get; set; } = string.Empty;

    public DateOnly OrderDate { get; set; }

    public OrderKind Kind { get; set; }

    /// <summary>Canal exibido pela origem; entra no agrupamento do relatório.</summary>
    public string? Channel { get; set; }

    /// <summary>Aba/origem (cota ou omni); entra no agrupamento do relatório.</summary>
    public OrderOrigin? Origin { get; set; }

    public ImportChangeAction Action { get; set; }

    /// <summary>Campos alterados, separados por vírgula, em <c>updated</c> (ex.: <c>total,status,items</c>).</summary>
    public string? ChangedFields { get; set; }

    /// <summary>Motivo em <c>error</c>; sempre sem dado pessoal (FR-052).</summary>
    public string? ErrorMessage { get; set; }

    public DateTime CreatedAt { get; set; }

    public ImportRun? ImportRun { get; set; }

    public ImportRunBlock? ImportRunBlock { get; set; }

    public Order? Order { get; set; }

    /// <summary>
    /// Monta a linha de um pedido coletado. Toda a sensibilidade fica de fora por construção: os
    /// parâmetros são a chave natural e os rótulos de agrupamento, e <see cref="CollectedOrder"/>
    /// nem é aceito aqui (o chamador já escolheu o que pode ser gravado).
    /// </summary>
    public static ImportRunChange From(
        long importRunId,
        long importRunBlockId,
        long? orderId,
        string orderNumber,
        DateOnly orderDate,
        OrderKind kind,
        string? channel,
        OrderOrigin? origin,
        ImportChangeAction action,
        DateTime now,
        IEnumerable<string>? changedFields = null,
        string? errorMessage = null) => new()
        {
            ImportRunId = importRunId,
            ImportRunBlockId = importRunBlockId,
            OrderId = orderId,
            OrderNumber = Truncate(orderNumber, 60) ?? string.Empty,
            OrderDate = orderDate,
            Kind = kind,
            Channel = Truncate(channel, 60),
            Origin = origin,
            Action = action,
            ChangedFields = changedFields is null
                ? null
                : Truncate(string.Join(',', changedFields), CHANGED_FIELDS_MAX_LENGTH),
            ErrorMessage = Truncate(errorMessage, ERROR_MESSAGE_MAX_LENGTH),
            CreatedAt = now
        };

    /// <summary>
    /// Linha de um pedido importado antes e ausente da origem nesta execução (FR-027): a chave natural
    /// vem do <b>banco</b>, não da coleta, e nada é alterado no pedido.
    /// </summary>
    public static ImportRunChange Missing(
        long importRunId,
        long importRunBlockId,
        long orderId,
        string orderNumber,
        DateOnly orderDate,
        OrderKind kind,
        string? channel,
        OrderOrigin? origin,
        DateTime now) => new()
        {
            ImportRunId = importRunId,
            ImportRunBlockId = importRunBlockId,
            OrderId = orderId,
            OrderNumber = Truncate(orderNumber, 60) ?? string.Empty,
            OrderDate = orderDate,
            Kind = kind,
            Channel = Truncate(channel, 60),
            Origin = origin,
            Action = ImportChangeAction.Missing,
            CreatedAt = now
        };

    /// <summary>Os campos alterados como lista (o DTO devolve <c>changedFields[]</c>); vazios quando não há.</summary>
    public List<string> ChangedFieldsList() =>
        string.IsNullOrWhiteSpace(ChangedFields)
            ? []
            : ChangedFields.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        var trimmed = value.Trim();

        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }
}
