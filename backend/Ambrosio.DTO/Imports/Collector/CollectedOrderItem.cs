namespace Ambrosio.DTO.Imports.Collector;

/// <summary>
/// Uma linha de produto de um pedido coletado (feature 006, data-model §6). Estrutura independente da
/// fonte: vem da resposta interna <c>client-data</c> ou do DOM, e o Domain não sabe a diferença
/// (research §10).
/// </summary>
public sealed class CollectedOrderItem
{
    /// <summary>Número da linha como a origem apresenta (1-based); ordena as linhas do pedido.</summary>
    public int LineNo { get; init; }

    public string? ProductName { get; init; }

    /// <summary>SKU/referência do produto — é o que faz o mesmo SKU repetir foto entre pedidos.</summary>
    public string? SkuRef { get; init; }

    public string? Size { get; init; }

    /// <summary>Preço unitário; negativo em devolução/cancelamento (FR-013 da feature 001).</summary>
    public decimal Price { get; init; }

    /// <summary>URL da foto na origem. É a chave da deduplicação por URL do pipeline de fotos (FR-040).</summary>
    public string? ImageUrl { get; init; }
}
