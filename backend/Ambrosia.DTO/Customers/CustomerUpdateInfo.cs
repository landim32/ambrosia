using System.Text.Json.Serialization;

namespace Ambrosia.DTO.Customers;

/// <summary>
/// Entrada de <c>PUT /customers/{id}</c> (FR-009). Só o nome é editável: o CPF fica imutável
/// nesta entrega, porque alterá-lo exigiria rehashar/recifrar e revalidar a unicidade.
/// </summary>
public class CustomerUpdateInfo
{
    /// <summary>Novo nome: obrigatório, 2 a 200 caracteres.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}
