using System.Text.Json.Serialization;

namespace Ambrosia.DTO.Orders;

/// <summary>
/// Entrada de uma linha de pedido (FR-015). <c>lineNo</c> não é aceito na entrada: a numeração é
/// atribuída pelo modelo <c>Order.AddItem</c> (FR-018).
/// </summary>
public class OrderItemInsertInfo
{
    /// <summary>Nome do produto na origem; no máximo 200 caracteres.</summary>
    [JsonPropertyName("productName")]
    public string? ProductName { get; set; }

    /// <summary>Referência/SKU do produto; no máximo 60 caracteres.</summary>
    [JsonPropertyName("skuRef")]
    public string? SkuRef { get; set; }

    /// <summary>Tamanho declarado na origem (ex.: <c>38</c>, <c>P</c>); no máximo 20.</summary>
    [JsonPropertyName("size")]
    public string? Size { get; set; }

    /// <summary>Preço unitário; negativo em devolução/cancelamento. Sem regra de sinal.</summary>
    [JsonPropertyName("price")]
    public decimal? Price { get; set; }

    /// <summary>URL absoluta http/https da imagem de origem; no máximo 500 caracteres.</summary>
    [JsonPropertyName("imageUrl")]
    public string? ImageUrl { get; set; }
}
