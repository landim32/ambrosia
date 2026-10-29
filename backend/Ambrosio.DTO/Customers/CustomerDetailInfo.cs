using System.Text.Json.Serialization;

namespace Ambrosio.DTO.Customers;

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

    /// <summary>
    /// Quantidade de fotos no álbum (feature 009). Contada no banco a cada detalhe, sem coluna
    /// desnormalizada: o álbum muda por inclusão e por exclusão, e um contador gravado seria mais uma
    /// oportunidade de divergir (data-model §6).
    /// </summary>
    [JsonPropertyName("photoCount")]
    public int PhotoCount { get; set; }
}
