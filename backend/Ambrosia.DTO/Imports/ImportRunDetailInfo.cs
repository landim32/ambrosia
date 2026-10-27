using System.Text.Json.Serialization;

namespace Ambrosia.DTO.Imports;

/// <summary>
/// Relatório completo de uma execução (feature 006): o <see cref="ImportRunInfo"/> mais os blocos, o
/// rastro pedido a pedido, o agrupamento do relatório e a comparação com a execução anterior. É a
/// resposta de <c>GET /runs/current</c>, <c>GET /runs/{runId}</c> e
/// <c>POST /runs/{runId}/cancel</c>.
/// </summary>
public class ImportRunDetailInfo : ImportRunInfo
{
    /// <summary>Blocos na ordem de execução (FR-009/FR-013).</summary>
    [JsonPropertyName("blocks")]
    public List<ImportRunBlockInfo> Blocks { get; set; } = [];

    /// <summary>Uma linha por pedido tocado, mais os não encontrados (FR-048).</summary>
    [JsonPropertyName("changes")]
    public List<ImportRunChangeInfo> Changes { get; set; } = [];

    /// <summary>Agrupamento canal × origem × tipo × ação (FR-049).</summary>
    [JsonPropertyName("breakdown")]
    public List<ImportRunBreakdownInfo> Breakdown { get; set; } = [];

    /// <summary>
    /// Quantos pedidos a última execução concluída/parcial <b>do mesmo período</b> viu
    /// (inseridos + atualizados + inalterados); <c>null</c> quando não há comparação (research §18).
    /// </summary>
    [JsonPropertyName("previousRunOrdersCount")]
    public int? PreviousRunOrdersCount { get; set; }

    /// <summary>
    /// <c>true</c> quando esta execução viu menos pedidos que a anterior do mesmo período — a tela
    /// avisa, porque a origem pode estar indisponível em vez de o período estar vazio (FR-051).
    /// </summary>
    [JsonPropertyName("countDropWarning")]
    public bool CountDropWarning { get; set; }
}
