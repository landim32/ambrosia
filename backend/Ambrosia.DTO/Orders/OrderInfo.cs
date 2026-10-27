using System.Text.Json.Serialization;
using Ambrosia.DTO.Enums;

namespace Ambrosia.DTO.Orders;

/// <summary>
/// Pedido em listagem (FR-017). Traz o nome do cliente (a listagem do +Vendas mostra quem comprou)
/// e a contagem de linhas, sem carregar as linhas.
/// </summary>
public class OrderInfo
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("customerId")]
    public long CustomerId { get; set; }

    /// <summary>Nome do cliente no momento da leitura (nunca o CPF em claro).</summary>
    [JsonPropertyName("customerName")]
    public string CustomerName { get; set; } = string.Empty;

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

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("total")]
    public decimal Total { get; set; }

    /// <summary>
    /// Como o pedido entrou no Ambrosia (feature 006): <c>manual</c> ou <c>maisVendas</c>. A tela usa
    /// para marcar o pedido importado, e a importação só sobrescreve os vindos da origem (FR-023).
    /// </summary>
    [JsonPropertyName("source")]
    public OrderSource Source { get; set; } = OrderSource.Manual;

    /// <summary>Quantidade de itens do pedido (evita trazer as linhas na listagem).</summary>
    [JsonPropertyName("itemsCount")]
    public int ItemsCount { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; }
}
