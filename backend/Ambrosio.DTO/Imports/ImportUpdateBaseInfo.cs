using System.Text.Json.Serialization;

namespace Ambrosio.DTO.Imports;

/// <summary>
/// Estado do "Atualizar base" nos defaults e entrada do
/// <c>POST /imports/mais-vendas/runs/update-base</c> (feature 008, FR-011 a FR-015, research R5).
/// <para>
/// Nos <b>defaults</b> o objeto descreve o que o servidor calcularia agora: disponível, com o período e
/// — quando indisponível — o motivo na frase única que a tela mostra como dica do botão. No
/// <b>corpo do POST</b> só existe <see cref="DryRun"/>: as datas nunca vêm de quem pede (FR-013).
/// </para>
/// </summary>
public class ImportUpdateBaseInfo
{
    /// <summary><c>true</c> quando há o que atualizar e o intervalo cabe no limite de meses.</summary>
    [JsonPropertyName("available")]
    public bool Available { get; set; }

    /// <summary>Início calculado: data do último pedido importado do +Vendas, inclusive.</summary>
    [JsonPropertyName("startDate")]
    public DateOnly? StartDate { get; set; }

    /// <summary>Fim calculado: hoje no fuso America/Sao_Paulo (research §11 da 006).</summary>
    [JsonPropertyName("endDate")]
    public DateOnly? EndDate { get; set; }

    /// <summary>
    /// Por que não está disponível (null quando disponível). Duas frases possíveis, do contrato:
    /// ainda não há pedidos importados, ou o intervalo passa de três meses e a vendedora precisa
    /// importar em partes escolhendo as datas (FR-015).
    /// </summary>
    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    /// <summary>
    /// <c>true</c> para simular sem gravar nada (FR-032 da 006). No defaults é ignorado — não há
    /// entrada do usuário ali.
    /// </summary>
    [JsonPropertyName("dryRun")]
    public bool DryRun { get; set; }
}
