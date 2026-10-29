using System.Text.Json.Serialization;

namespace Ambrosio.DTO.Imports;

/// <summary>
/// Corpo do <c>POST /imports/mais-vendas/runs</c> (feature 006 FR-003; <b>feature 008</b>, research R7
/// e contrato): o período e o modo, nada além disso.
/// <para>
/// <b>Não existe mais campo de login nem de senha aqui</b> (FR-001/FR-002 da 008 substituem FR-003 da
/// 006): a vendedora cadastra as credenciais uma vez na tela de configuração, e o início usa o que está
/// guardado. O <c>400</c> de "campos de credencial no corpo" que o contrato previa não é uma regra
/// escrita em nenhum lugar — é a consequência direta deste objeto não ter a propriedade: o binding do
/// ASP.NET descarta o que não conhece, então um POST antigo não é recusado, apenas ignorado, e a
/// recusa vem da checagem de credenciais do serviço
/// (<c>IMaisVendasCredentialService.NOT_SAVED_MESSAGE</c>). Registrar isso aqui porque a diferença
/// entre "recusado" e "ignorado" importa para quem lê o contrato.
/// </para>
/// <para>
/// O <c>DataType.Password</c> continua no projeto apenas em
/// <see cref="MaisVendasCredentialsUpdateInfo"/>, que é quem de fato recebe a senha.
/// </para>
/// </summary>
public class ImportRunStartInfo
{
    /// <summary>Início do período, inclusive. Sem valor de ontem: a vendedora escolhe (FR-002).</summary>
    [JsonPropertyName("startDate")]
    public DateOnly StartDate { get; set; }

    /// <summary>Fim do período, inclusive; não pode ser futuro (FR-003).</summary>
    [JsonPropertyName("endDate")]
    public DateOnly EndDate { get; set; }

    /// <summary>
    /// <c>true</c> para simulação: percorre a origem e reporta o que mudaria sem gravar nada (FR-032).
    /// É a escolha da vendedora na tela, sempre explícito no corpo.
    /// </summary>
    [JsonPropertyName("dryRun")]
    public bool DryRun { get; set; }
}
