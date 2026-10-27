using System.Text.Json.Serialization;

namespace Ambrosio.DTO.Customers;

/// <summary>
/// Parâmetros de <c>GET /customers</c> (FR-008, research §9). Os padrões do DTO valem quando o
/// cliente não envia o parâmetro.
/// </summary>
public class CustomerListQuery
{
    /// <summary>Trecho do nome, comparado sem diferenciar maiúsculas.</summary>
    [JsonPropertyName("search")]
    public string? Search { get; set; }

    /// <summary>Página 1-based (mínimo 1).</summary>
    [JsonPropertyName("page")]
    public int Page { get; set; } = 1;

    /// <summary>Itens por página, entre 1 e 100.</summary>
    [JsonPropertyName("pageSize")]
    public int PageSize { get; set; } = 20;
}
