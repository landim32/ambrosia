using System.Text.Json.Serialization;

namespace Ambrosia.DTO.Imports;

/// <summary>
/// Parâmetros de <c>GET /imports/mais-vendas/runs</c> (feature 006): só paginação — o histórico é a
/// execução da própria vendedora, ordenado por <c>requestedAt</c> decrescente.
/// </summary>
public class ImportRunListQuery
{
    /// <summary>Página 1-based (mínimo 1).</summary>
    [JsonPropertyName("page")]
    public int Page { get; set; } = 1;

    /// <summary>Itens por página, entre 1 e 50.</summary>
    [JsonPropertyName("pageSize")]
    public int PageSize { get; set; } = 10;
}
