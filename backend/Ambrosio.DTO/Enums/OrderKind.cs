namespace Ambrosio.DTO.Enums;

/// <summary>
/// Tipo de pedido. Persistido como <c>integer</c> (constituição §V) e exposto no JSON como
/// string minúscula (<c>sale</c>, <c>return</c>, <c>cancellation</c>) pelo JsonStringEnumConverter.
/// </summary>
public enum OrderKind
{
    /// <summary>Venda: total obrigatoriamente positivo.</summary>
    Sale = 1,

    /// <summary>Devolução: total obrigatoriamente negativo.</summary>
    Return = 2,

    /// <summary>Cancelamento: total obrigatoriamente negativo.</summary>
    Cancellation = 3
}
