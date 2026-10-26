using System.Text.Json.Serialization;

namespace Ambrosia.DTO.Common;

/// <summary>
/// Wrapper de erro padrão da API (constituição §IV, FR-024): chaves <c>sucesso</c>,
/// <c>mensagem</c> e <c>erros</c>, sempre em português.
/// </summary>
public class ErrorResult
{
    /// <summary>Sempre <c>false</c> em uma resposta de erro.</summary>
    [JsonPropertyName("sucesso")]
    public bool Sucesso { get; set; } = false;

    /// <summary>Mensagem geral do erro.</summary>
    [JsonPropertyName("mensagem")]
    public string Mensagem { get; set; } = string.Empty;

    /// <summary>Detalhamento por campo. Vazio em erros que não apontam um campo (404/409).</summary>
    [JsonPropertyName("erros")]
    public List<FieldError> Erros { get; set; } = [];
}
