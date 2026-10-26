using Ambrosia.DTO.Common;
using Ambrosia.DTO.Orders;

namespace Ambrosia.Domain.Interfaces;

/// <summary>
/// Cadastro e consulta de pedidos da vendedora autenticada (FR-012 a FR-019). Como em
/// <see cref="ICustomerService"/>, todo método recebe o <c>userId</c> do token (FR-021,
/// research §13).
/// </summary>
public interface IOrderService
{
    /// <summary>Listagem filtrada, ordenada por data decrescente e paginada (FR-017).</summary>
    Task<PagedResult<OrderInfo>> ListAsync(long userId, OrderListQuery query);

    /// <summary>Detalhe com cliente (CPF mascarado) e itens numerados (FR-016).</summary>
    Task<OrderDetailInfo> GetByIdAsync(long userId, long orderId);

    /// <summary>Cadastro do pedido com itens (FR-012 a FR-015, FR-023).</summary>
    Task<OrderDetailInfo> CreateAsync(long userId, OrderInsertInfo info);

    /// <summary>Atualização de status e total (FR-019).</summary>
    Task<OrderDetailInfo> UpdateAsync(long userId, long orderId, OrderUpdateInfo info);
}
