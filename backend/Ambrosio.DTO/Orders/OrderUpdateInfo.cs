using System.Text.Json.Serialization;

namespace Ambrosio.DTO.Orders;

/// <summary>
/// Entrada de <c>PUT /orders/{id}</c> (FR-019). Apenas status e total são editáveis:
/// <c>kind</c> é imutável (um cancelamento é um novo pedido) e o sinal do total é revalidado
/// contra o tipo gravado.
/// </summary>
public class OrderUpdateInfo
{
    /// <summary>Novo status (texto livre, máximo 60). Opcional.</summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary>Novo total, diferente de zero e com o sinal do tipo gravado.</summary>
    [JsonPropertyName("total")]
    public decimal Total { get; set; }
}
