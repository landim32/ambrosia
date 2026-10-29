using System.Text.Json.Serialization;
using Ambrosio.DTO.Enums;

namespace Ambrosio.DTO.Imports;

/// <summary>
/// Resposta de <c>GET /imports/mais-vendas/defaults</c> (feature 006 FR-002; feature 008 R5/R7): o
/// ponto de partida da tela, para a vendedora não ter de decorar até onde importou — e para a tela
/// saber se pode iniciar sem perguntar a senha.
/// <para>
/// <b>Nenhum campo de credencial</b> (FR-003): <see cref="HasCredentials"/> é um booleano e
/// <see cref="CredentialsStatus"/> é o estado; login e senha não passam por aqui.
/// </para>
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

    /// <summary>
    /// Limite do período em meses corridos (feature 008, research R6; substitui <c>maxRangeDays</c>),
    /// para a tela e o plugin repetirem a mesma conta e a mesma mensagem (SC-005).
    /// </summary>
    [JsonPropertyName("maxRangeMonths")]
    public int MaxRangeMonths { get; set; }

    /// <summary>
    /// <c>true</c> quando a vendedora tem login e senha guardados — sem eles o início é recusado e a
    /// tela mostra o link da configuração (FR-009/FR-010).
    /// </summary>
    [JsonPropertyName("hasCredentials")]
    public bool HasCredentials { get; set; }

    /// <summary>
    /// Situação das credenciais (<c>none</c> | <c>valid</c> | <c>needsUpdate</c>).
    /// <c>needsUpdate</c> gera aviso, nunca bloqueio (FR-007).
    /// </summary>
    [JsonPropertyName("credentialsStatus")]
    public MaisVendasCredentialsStatus CredentialsStatus { get; set; }

    /// <summary>
    /// Até quando novos inícios estão bloqueados por recusas seguidas de credencial (FR-008a);
    /// <c>null</c> quando não há bloqueio. A tela desabilita o envio e mostra o horário.
    /// </summary>
    [JsonPropertyName("lockedUntil")]
    public DateTime? LockedUntil { get; set; }

    /// <summary>"Atualizar base" (feature 008, FR-011/FR-013): período já calculado pelo servidor.</summary>
    [JsonPropertyName("updateBase")]
    public ImportUpdateBaseInfo UpdateBase { get; set; } = new();
}
