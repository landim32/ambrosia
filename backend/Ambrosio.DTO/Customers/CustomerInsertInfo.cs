using System.Text.Json.Serialization;

namespace Ambrosio.DTO.Customers;

/// <summary>Entrada de <c>POST /customers</c> (FR-006, FR-007).</summary>
public class CustomerInsertInfo
{
    /// <summary>Nome do comprador: obrigatório, 2 a 200 caracteres.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>CPF com ou sem pontuação. Nunca é gravado em claro (FR-011).</summary>
    [JsonPropertyName("cpf")]
    public string Cpf { get; set; } = string.Empty;
}
