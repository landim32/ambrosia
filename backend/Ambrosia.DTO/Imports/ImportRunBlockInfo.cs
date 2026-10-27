using System.Text.Json.Serialization;
using Ambrosia.DTO.Enums;

namespace Ambrosia.DTO.Imports;

/// <summary>
/// Fatia do período coletada de uma vez (feature 006, FR-009): é a linha de
/// <c>import_run_blocks</c> traduzida para a tela, com as contagens próprias — é o que permite ao
/// relatório dizer "o bloco 3 falhou com 14 pedidos gravados" (US3 cenário 1; o número de pedidos
/// gravados sai de <c>counts.inserted + updated + unchanged</c>).
/// </summary>
public class ImportRunBlockInfo
{
    [JsonPropertyName("blockNo")]
    public int BlockNo { get; set; }

    [JsonPropertyName("startDate")]
    public DateOnly StartDate { get; set; }

    [JsonPropertyName("endDate")]
    public DateOnly EndDate { get; set; }

    [JsonPropertyName("status")]
    public ImportBlockStatus Status { get; set; }

    [JsonPropertyName("counts")]
    public ImportRunCountsInfo Counts { get; set; } = new();

    /// <summary>Motivo quando <c>failed</c>; sem login, senha ou CPF (FR-052).</summary>
    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }

    [JsonPropertyName("startedAt")]
    public DateTime? StartedAt { get; set; }

    [JsonPropertyName("finishedAt")]
    public DateTime? FinishedAt { get; set; }
}
