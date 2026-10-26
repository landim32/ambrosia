using System.Text.Json.Serialization;
using Ambrosia.DTO.Customers;

namespace Ambrosia.DTO.Orders;

/// <summary>
/// Detalhe do pedido (FR-016): os campos da listagem mais as datas do cancelamento, o identificador
/// externo, o cliente (somente com CPF mascarado — SC-005) e as linhas numeradas.
/// </summary>
public class OrderDetailInfo : OrderInfo
{
    /// <summary>Data da venda original, quando informada.</summary>
    [JsonPropertyName("originalSaleOn")]
    public DateOnly? OriginalSaleOn { get; set; }

    /// <summary>Data do cancelamento, quando informada.</summary>
    [JsonPropertyName("cancelledOn")]
    public DateOnly? CancelledOn { get; set; }

    /// <summary>Identificador externo de origem.</summary>
    [JsonPropertyName("externalId")]
    public string? ExternalId { get; set; }

    /// <summary>Cliente do pedido. <see cref="CustomerInfo"/> não tem campo de CPF completo.</summary>
    [JsonPropertyName("customer")]
    public CustomerInfo Customer { get; set; } = new();

    /// <summary>Linhas ordenadas por <c>lineNo</c> (FR-018).</summary>
    [JsonPropertyName("items")]
    public List<OrderItemInfo> Items { get; set; } = [];
}
