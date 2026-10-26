namespace Ambrosia.DTO.Enums;

/// <summary>
/// Origem do pedido (canal de fechamento do +Vendas). Persistido como <c>integer</c>;
/// no JSON: <c>cota</c> | <c>omni</c>.
/// </summary>
public enum OrderOrigin
{
    /// <summary>Venda por cota.</summary>
    Cota = 1,

    /// <summary>Venda omni-channel.</summary>
    Omni = 2
}
