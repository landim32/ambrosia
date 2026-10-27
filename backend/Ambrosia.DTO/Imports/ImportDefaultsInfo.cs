using System.Text.Json.Serialization;

namespace Ambrosia.DTO.Imports;

/// <summary>
/// Resposta de <c>GET /imports/mais-vendas/defaults</c> (FR-002): o ponto de partida da tela, para
/// a vendedora não ter de decorar até onde importou.
/// </summary>
public class ImportDefaultsInfo
{
    /// <summary>
    /// Data do último pedido importado do +Vendas, ou <c>null</c> quando não há nenhum — nesse caso a
    /// tela deixa a data inicial vazia e obrigatória (US1 cenário 2).
    /// </summary>
    [JsonPropertyName("suggestedStartDate")]
    public DateOnly? SuggestedStartDate { get; set; }

    /// <summary>Hoje no fuso America/Sao_Paulo (research §11).</summary>
    [JsonPropertyName("suggestedEndDate")]
    public DateOnly SuggestedEndDate { get; set; }

    /// <summary>Tamanho do bloco de coleta em dias, vigente na configuração.</summary>
    [JsonPropertyName("blockDays")]
    public int BlockDays { get; set; }

    /// <summary>Limite do período em dias, para a mensagem de validação citar o número certo (FR-004).</summary>
    [JsonPropertyName("maxRangeDays")]
    public int MaxRangeDays { get; set; }

    /// <summary>
    /// Até quando novos inícios estão bloqueados por recusas seguidas de credencial (FR-008a);
    /// <c>null</c> quando não há bloqueio. A tela desabilita o envio e mostra o horário.
    /// </summary>
    [JsonPropertyName("lockedUntil")]
    public DateTime? LockedUntil { get; set; }
}
