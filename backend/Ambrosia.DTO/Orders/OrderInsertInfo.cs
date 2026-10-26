using System.Text.Json.Serialization;
using Ambrosia.DTO.Enums;

namespace Ambrosia.DTO.Orders;

/// <summary>
/// Entrada de <c>POST /orders</c> (FR-012 a FR-015). Não há <c>userId</c> nem <c>lineNo</c> na
/// entrada: o dono vem do token (FR-021) e a numeração das linhas é do modelo (FR-018).
/// </summary>
public class OrderInsertInfo
{
    /// <summary>Cliente do pedido, que DEVE pertencer ao usuário (FR-023 — verificado no serviço).</summary>
    [JsonPropertyName("customerId")]
    public long CustomerId { get; set; }

    /// <summary>Número de origem (<c>00004751</c>, <c>v1161148crb-01</c>); não é único sozinho.</summary>
    [JsonPropertyName("orderNumber")]
    public string OrderNumber { get; set; } = string.Empty;

    /// <summary>Data do pedido; nunca futura (FR-012).</summary>
    [JsonPropertyName("orderDate")]
    public DateOnly OrderDate { get; set; }

    /// <summary>Venda, devolução ou cancelamento (research §7).</summary>
    [JsonPropertyName("kind")]
    public OrderKind Kind { get; set; }

    /// <summary>Canal de origem. Texto livre: canais novos do +Vendas são aceitos (SC-006).</summary>
    [JsonPropertyName("channel")]
    public string? Channel { get; set; }

    /// <summary>Origem fechada (<c>cota</c>/<c>omni</c>), opcional.</summary>
    [JsonPropertyName("origin")]
    public OrderOrigin? Origin { get; set; }

    /// <summary>Status de origem. Texto livre (plan.md, Complexity Tracking).</summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary>&gt; 0 em venda; &lt; 0 em devolução/cancelamento; nunca 0 (FR-013).</summary>
    [JsonPropertyName("total")]
    public decimal Total { get; set; }

    /// <summary>Cancelamento/devolução: data da venda original.</summary>
    [JsonPropertyName("originalSaleOn")]
    public DateOnly? OriginalSaleOn { get; set; }

    /// <summary>Cancelamento: data do cancelamento.</summary>
    [JsonPropertyName("cancelledOn")]
    public DateOnly? CancelledOn { get; set; }

    /// <summary>Identificador externo (4º segmento da URL do +Vendas).</summary>
    [JsonPropertyName("externalId")]
    public string? ExternalId { get; set; }

    /// <summary>Linhas do pedido: ao menos uma (FR-015).</summary>
    [JsonPropertyName("items")]
    public List<OrderItemInsertInfo> Items { get; set; } = [];
}
