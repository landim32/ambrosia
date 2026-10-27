using System.Text.Json.Serialization;

namespace Ambrosio.DTO.Common;

/// <summary>
/// Erro de validação de um único campo (FR-024). As chaves do contrato são em português:
/// <c>campo</c> e <c>mensagem</c>.
/// </summary>
public class FieldError
{
    /// <summary>Nome do campo inválido em camelCase (ex.: <c>cpf</c>, <c>items[0].imageUrl</c>).</summary>
    [JsonPropertyName("campo")]
    public string Campo { get; set; } = string.Empty;

    /// <summary>Mensagem de erro em português.</summary>
    [JsonPropertyName("mensagem")]
    public string Mensagem { get; set; } = string.Empty;
}
