using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Ambrosia.DTO.Imports;

/// <summary>
/// Entrada de <c>POST /imports/mais-vendas/runs</c> (feature 006, contrato imports-api.md): as
/// credenciais do +Vendas <b>para esta execução</b> e o período desejado.
/// <para>
/// É o único lugar em que <see cref="Login"/> e <see cref="Password"/> existem fora do job em memória
/// (research §14). Nada daqui é persistido: o controller não loga o corpo, as mensagens de validação
/// nunca ecoam o valor enviado, e <c>[DataType(DataType.Password)]</c> faz o Swashbuckle documentar
/// <c>password</c> com <c>format: password</c> (FR-006, SC-004).
/// </para>
/// </summary>
public class ImportRunStartInfo
{
    /// <summary>Login da vendedora no +Vendas (em homologação, no formato <c>conta+cenario</c>).</summary>
    [JsonPropertyName("login")]
    public string Login { get; set; } = string.Empty;

    /// <summary>
    /// Senha do +Vendas. Usada só enquanto a execução está em andamento e descartada no fim, com
    /// sucesso, falha ou cancelamento (FR-005); <c>ToString()</c> do objeto que a carrega nunca a
    /// reproduz.
    /// </summary>
    [JsonPropertyName("password")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    /// <summary>Primeiro dia do período, inclusive (FR-004).</summary>
    [JsonPropertyName("startDate")]
    public DateOnly StartDate { get; set; }

    /// <summary>Último dia do período, inclusive; nunca no futuro (FR-004).</summary>
    [JsonPropertyName("endDate")]
    public DateOnly EndDate { get; set; }

    /// <summary>
    /// <c>true</c> para simular sem gravar nada (FR-032, US4). <c>false</c> grava de fato.
    /// </summary>
    [JsonPropertyName("dryRun")]
    public bool DryRun { get; set; }
}
