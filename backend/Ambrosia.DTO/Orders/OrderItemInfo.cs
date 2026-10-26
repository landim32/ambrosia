using System.Text.Json.Serialization;

namespace Ambrosia.DTO.Orders;

/// <summary>Linha de pedido na resposta (FR-016): idents da persistência + campos de origem.</summary>
public class OrderItemInfo
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    /// <summary>Número da linha (1-based), atribuído pelo modelo (FR-018).</summary>
    [JsonPropertyName("lineNo")]
    public int LineNo { get; set; }

    [JsonPropertyName("productName")]
    public string? ProductName { get; set; }

    [JsonPropertyName("skuRef")]
    public string? SkuRef { get; set; }

    [JsonPropertyName("size")]
    public string? Size { get; set; }

    [JsonPropertyName("price")]
    public decimal? Price { get; set; }

    [JsonPropertyName("imageUrl")]
    public string? ImageUrl { get; set; }
}
