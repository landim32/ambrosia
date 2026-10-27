using System.Text.Json.Serialization;

namespace Ambrosio.DTO.Customers;

/// <summary>
/// Cliente em listagem (FR-008). Traz apenas <see cref="CpfMasked"/>: o documento completo só
/// aparece no detalhe (SC-005).
/// </summary>
public class CustomerInfo
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>CPF mascarado: <c>***.982.247-**</c> (dígitos 4 a 9 visíveis).</summary>
    [JsonPropertyName("cpfMasked")]
    public string CpfMasked { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; }
}
