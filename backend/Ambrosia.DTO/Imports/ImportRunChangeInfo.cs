using System.Text.Json.Serialization;
using Ambrosia.DTO.Enums;

namespace Ambrosia.DTO.Imports;

/// <summary>
/// Uma linha do rastro da execução (feature 006, FR-048): o que aconteceu com <b>um</b> pedido.
/// <para>
/// Traz só a chave natural do pedido — <b>nunca</b> o CPF ou o nome do cliente (FR-052), porque a
/// tabela <c>import_run_changes</c> também não guarda. <see cref="OrderId"/> existe para o link
/// <c>/pedidos/{orderId}</c> da tela (FR-049) e é <c>null</c> em simulação, em <c>error</c> e em
/// <c>missing</c> quando o pedido não está no banco.
/// </para>
/// </summary>
public class ImportRunChangeInfo
{
    [JsonPropertyName("orderId")]
    public long? OrderId { get; set; }

    [JsonPropertyName("orderNumber")]
    public string OrderNumber { get; set; } = string.Empty;

    [JsonPropertyName("orderDate")]
    public DateOnly OrderDate { get; set; }

    [JsonPropertyName("kind")]
    public OrderKind Kind { get; set; }

    [JsonPropertyName("channel")]
    public string? Channel { get; set; }

    [JsonPropertyName("origin")]
    public OrderOrigin? Origin { get; set; }

    /// <summary>Decisão da importação para este pedido (FR-048).</summary>
    [JsonPropertyName("action")]
    public ImportChangeAction Action { get; set; }

    /// <summary>
    /// Campos que mudaram, em <c>updated</c> (ex.: <c>total</c>, <c>status</c>, <c>items</c>) — FR-023.
    /// <c>null</c> nas demais ações.
    /// </summary>
    [JsonPropertyName("changedFields")]
    public List<string>? ChangedFields { get; set; }

    /// <summary>Motivo em <c>error</c> (ex.: "CPF ausente ou inválido na origem."), sempre sem CPF.</summary>
    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }
}
