using System.Text.Json.Serialization;

namespace Ambrosia.DTO.Customers;

/// <summary>
/// Detalhe do cliente (FR-009): os campos da listagem mais o CPF completo decifrado e o resumo
/// de compras. É a única resposta que carrega o documento inteiro.
/// </summary>
public class CustomerDetailInfo : CustomerInfo
{
    /// <summary>CPF decifrado e formatado (<c>529.982.247-25</c>), somente no detalhe (SC-005).</summary>
    [JsonPropertyName("cpf")]
    public string Cpf { get; set; } = string.Empty;

    /// <summary>Resumo de compras calculado sobre os pedidos do cliente.</summary>
    [JsonPropertyName("summary")]
    public CustomerSummaryInfo Summary { get; set; } = new();
}
