using System.Text.Json.Serialization;
using Ambrosia.DTO.Enums;

namespace Ambrosia.DTO.Imports;

/// <summary>
/// Agrupamento do relatório por canal × origem × tipo × ação (feature 006, FR-049). Derivado de
/// <c>import_run_changes</c> no banco, para a tela não somar trezentas linhas.
/// </summary>
public class ImportRunBreakdownInfo
{
    [JsonPropertyName("channel")]
    public string? Channel { get; set; }

    [JsonPropertyName("origin")]
    public OrderOrigin? Origin { get; set; }

    [JsonPropertyName("kind")]
    public OrderKind Kind { get; set; }

    [JsonPropertyName("action")]
    public ImportChangeAction Action { get; set; }

    [JsonPropertyName("count")]
    public int Count { get; set; }
}
