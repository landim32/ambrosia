using Ambrosia.DTO.Enums;

namespace Ambrosia.Domain.Common;

/// <summary>
/// Regra de sinal do total por tipo de pedido (FR-013), usada pelo validator da inserção, pelo
/// agregado <c>Order</c> e pelo serviço na atualização — um único lugar para a mensagem em
/// português citando o campo (FR-024).
/// </summary>
public static class OrderTotalSignRule
{
    /// <summary>
    /// Venda exige total positivo; devolução e cancelamento exigem negativo; zero nunca é válido.
    /// </summary>
    public static bool IsValid(OrderKind kind, decimal total) => kind switch
    {
        OrderKind.Sale => total > 0m,
        OrderKind.Return => total < 0m,
        OrderKind.Cancellation => total < 0m,
        _ => false
    };

    /// <summary>Mensagem do erro esperada para o tipo (campo <c>total</c>).</summary>
    public static string ErrorMessage(OrderKind kind) => kind switch
    {
        OrderKind.Sale => "O total de uma venda deve ser positivo.",
        OrderKind.Return => "O total de uma devolução ou cancelamento deve ser negativo.",
        OrderKind.Cancellation => "O total de uma devolução ou cancelamento deve ser negativo.",
        _ => "O tipo do pedido é inválido."
    };
}
