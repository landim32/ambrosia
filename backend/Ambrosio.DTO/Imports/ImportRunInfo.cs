using System.Text.Json.Serialization;
using Ambrosio.DTO.Enums;

namespace Ambrosio.DTO.Imports;

/// <summary>
/// Resumo de uma execução de importação (feature 006): o corpo do <c>202</c> do
/// <c>POST /runs</c>, o item de <c>GET /runs</c> e a base do detalhe.
/// <para>
/// <b>Garantia transversal (FR-006/SC-004)</b>: este DTO não tem — e nunca ganha — campo de login,
/// senha ou hash da conta do +Vendas. O que sai da API sobre credenciais é no máximo o
/// <c>errorMessage</c> genérico e o <c>lockedUntil</c>.
/// </para>
/// </summary>
public class ImportRunInfo
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    /// <summary><c>dryRun</c> (simulação) ou <c>apply</c> (gravação).</summary>
    [JsonPropertyName("mode")]
    public ImportRunMode Mode { get; set; }

    /// <summary>Estado atual, conforme FR-047a (research §18).</summary>
    [JsonPropertyName("status")]
    public ImportRunStatus Status { get; set; }

    [JsonPropertyName("startDate")]
    public DateOnly StartDate { get; set; }

    [JsonPropertyName("endDate")]
    public DateOnly EndDate { get; set; }

    /// <summary>Tamanho do bloco em dias vigente na execução.</summary>
    [JsonPropertyName("blockDays")]
    public int BlockDays { get; set; }

    [JsonPropertyName("blocksTotal")]
    public int BlocksTotal { get; set; }

    /// <summary>Blocos já terminados (concluído, falho ou não executado), para "2 de 5".</summary>
    [JsonPropertyName("blocksFinished")]
    public int BlocksFinished { get; set; }

    /// <summary>Número do bloco em curso; <c>null</c> quando não há bloco rodando.</summary>
    [JsonPropertyName("currentBlockNo")]
    public int? CurrentBlockNo { get; set; }

    /// <summary>Cancelamento pedido pela vendedora: encerra ao fim do bloco atual (FR-019).</summary>
    [JsonPropertyName("cancelRequested")]
    public bool CancelRequested { get; set; }

    [JsonPropertyName("requestedAt")]
    public DateTime RequestedAt { get; set; }

    [JsonPropertyName("startedAt")]
    public DateTime? StartedAt { get; set; }

    [JsonPropertyName("finishedAt")]
    public DateTime? FinishedAt { get; set; }

    /// <summary>Duração em segundos, calculada; <c>null</c> enquanto não terminou.</summary>
    [JsonPropertyName("durationSeconds")]
    public long? DurationSeconds { get; set; }

    [JsonPropertyName("counts")]
    public ImportRunCountsInfo Counts { get; set; } = new();

    /// <summary>
    /// Motivo quando <c>failed</c>/<c>cancelled</c>. <b>Nunca</b> contém login, senha ou CPF
    /// (FR-052, research §14).
    /// </summary>
    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }
}
