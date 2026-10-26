using System.Text.Json.Serialization;

namespace Ambrosia.DTO.Common;

/// <summary>
/// Página de resultados de uma listagem (FR-008, FR-017). Página além do fim devolve
/// <see cref="Items"/> vazio com <see cref="TotalCount"/> correto.
/// <para>
/// Este tipo genérico não conhece nada da camada de apresentação; o nome do schema no documento
/// OpenAPI (<c>PagedCustomerInfo</c> / <c>PagedOrderInfo</c>, como em
/// <c>contracts/openapi.yaml</c>) é decidido pela API em <c>Program.cs</c> via
/// <c>CustomSchemaIds</c> (T094), para não vazar convenção de Swagger para o DTO.
/// </para>
/// </summary>
/// <typeparam name="T">Tipo do item da listagem.</typeparam>
public class PagedResult<T>
{
    /// <summary>Itens da página solicitada.</summary>
    [JsonPropertyName("items")]
    public List<T> Items { get; set; } = [];

    /// <summary>Número da página retornada (1-based).</summary>
    [JsonPropertyName("page")]
    public int Page { get; set; }

    /// <summary>Quantidade de itens por página.</summary>
    [JsonPropertyName("pageSize")]
    public int PageSize { get; set; }

    /// <summary>Total de registros que atendem ao filtro, independentemente da paginação.</summary>
    [JsonPropertyName("totalCount")]
    public int TotalCount { get; set; }
}
