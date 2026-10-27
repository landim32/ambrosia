using System.Text.Json.Serialization;
using Ambrosio.DTO.Enums;

namespace Ambrosio.DTO.Orders;

/// <summary>
/// Parâmetros de <c>GET /orders</c> (FR-017, research §9). Datas inclusivas, <c>channel</c> por
/// igualdade sem diferenciar maiúsculas e <c>search</c> sobre número do pedido OU nome do cliente.
/// </summary>
public class OrderListQuery
{
    /// <summary>Data inicial inclusiva.</summary>
    [JsonPropertyName("from")]
    public DateOnly? From { get; set; }

    /// <summary>Data final inclusiva.</summary>
    [JsonPropertyName("to")]
    public DateOnly? To { get; set; }

    /// <summary>Filtro por tipo (sale/return/cancellation).</summary>
    [JsonPropertyName("kind")]
    public OrderKind? Kind { get; set; }

    /// <summary>Canal, por igualdade case-insensitive (<c>linx</c> encontra <c>Linx</c>).</summary>
    [JsonPropertyName("channel")]
    public string? Channel { get; set; }

    /// <summary>Restringe os pedidos de um cliente.</summary>
    [JsonPropertyName("customerId")]
    public long? CustomerId { get; set; }

    /// <summary>Trecho do número do pedido ou do nome do cliente.</summary>
    [JsonPropertyName("search")]
    public string? Search { get; set; }

    /// <summary>Página 1-based (mínimo 1).</summary>
    [JsonPropertyName("page")]
    public int Page { get; set; } = 1;

    /// <summary>Itens por página, entre 1 e 100.</summary>
    [JsonPropertyName("pageSize")]
    public int PageSize { get; set; } = 20;
}
