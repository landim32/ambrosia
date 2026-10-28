using Ambrosio.DTO.Customers;
using Ambrosio.DTO.Enums;
using Ambrosio.DTO.Insights;
using Ambrosio.DTO.Orders;

namespace Ambrosio.Infra.Interfaces.Repository;

/// <summary>
/// Persistência de pedidos (tabela <c>orders</c> + <c>order_items</c>).
/// <para>
/// Isolamento estrutural (FR-021, research §13): todo método de leitura exige <c>userId</c> e a
/// implementação aplica <c>Where(o => o.UserId == userId)</c> como primeiro filtro — inclusive nos
/// <c>Include</c> que carregam o cliente.
/// </para>
/// </summary>
/// <typeparam name="TModel">Modelo do pedido.</typeparam>
public interface IOrderRepository<TModel> where TModel : class
{
    /// <summary>
    /// Pedido pelo id, com cliente e itens (linhas ordenadas por <c>lineNo</c>) — apenas se for do
    /// usuário; <c>null</c> nos dois casos (FR-016, FR-022).
    /// </summary>
    Task<TModel?> GetByIdAsync(long orderId, long userId);

    /// <summary>Duplicidade pelo quádruplo <c>(userId, orderNumber, orderDate, kind)</c> (FR-014).</summary>
    Task<bool> ExistsAsync(long userId, string orderNumber, DateOnly orderDate, OrderKind kind);

    /// <summary>Listagem filtrada, ordenada por data decrescente e paginada (FR-017).</summary>
    Task<(List<TModel> Items, int TotalCount)> ListAsync(long userId, OrderListQuery query);

    Task<TModel> InsertAsync(TModel entity);

    Task<TModel> UpdateAsync(TModel entity);

    /// <summary>
    /// Líquido, atendimentos (vendas) e peças (itens das vendas) do usuário no período (feature 007,
    /// T050), calculados no banco.
    /// </summary>
    Task<(decimal NetTotal, int Sales, int Pieces)> GetPeriodSummaryAsync(long userId, DateOnly from, DateOnly to);

    /// <summary>Contagem e soma por tipo (devolução e cancelamento) no período.</summary>
    Task<Dictionary<OrderKind, KindTotalsInfo>> GetReturnsCancellationsAsync(long userId, DateOnly from, DateOnly to);

    /// <summary>Pedidos de uma cliente com itens, mais recentes primeiro, até <paramref name="limit"/>.</summary>
    Task<(List<TModel> Items, int TotalCount)> ListByCustomerWithItemsAsync(long userId, long customerId, int limit);
}
