using Ambrosia.Domain.Exceptions;
using Ambrosia.Domain.Models;
using Ambrosia.DTO.Enums;
using Ambrosia.DTO.Orders;
using Ambrosia.Infra.Context;
using Ambrosia.Infra.Interfaces.Repository;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ambrosia.Infra.Repository;

/// <summary>
/// Acesso à tabela <c>orders</c> (+ <c>order_items</c>) com isolamento estrutural (FR-021,
/// research §13): toda consulta começa por <c>Where(o => o.UserId == userId)</c> — inclusive a que
/// traz o cliente, para que um pedido nunca revele dados de outro usuário.
/// </summary>
public class OrderRepository : IOrderRepository<Order>
{
    /// <summary>SQLSTATE de violação de unicidade no PostgreSQL (research §11).</summary>
    private const string UniqueViolation = "23505";

    private readonly AmbrosiaContext _context;

    public OrderRepository(AmbrosiaContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task<Order?> GetByIdAsync(long orderId, long userId)
    {
        var order = await _context.Orders
            .AsNoTracking()
            .Where(o => o.UserId == userId)
            .Include(o => o.Customer)
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId);

        if (order is not null)
        {
            SortItems(order);
        }

        return order;
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(long userId, string orderNumber, DateOnly orderDate, OrderKind kind)
    {
        // FR-014: o mesmo número pode se repetir trocando a data OU o tipo — a venda e o
        // cancelamento do pedido 00004751 coexistem (quickstart, Cenário 3).
        return await _context.Orders
            .AsNoTracking()
            .Where(o => o.UserId == userId)
            .AnyAsync(o => o.OrderNumber == orderNumber
                           && o.OrderDate == orderDate
                           && o.Kind == kind);
    }

    /// <inheritdoc />
    public async Task<(List<Order> Items, int TotalCount)> ListAsync(long userId, OrderListQuery query)
    {
        // ix_orders_user_date (user_id, order_date DESC) atende o filtro por dono + a ordenação
        // (SC-003).
        var scoped = _context.Orders
            .AsNoTracking()
            .Where(o => o.UserId == userId)
            .AsQueryable();

        if (query.From is { } from)
        {
            scoped = scoped.Where(o => o.OrderDate >= from);
        }

        if (query.To is { } to)
        {
            scoped = scoped.Where(o => o.OrderDate <= to);
        }

        if (query.Kind is { } kind)
        {
            scoped = scoped.Where(o => o.Kind == kind);
        }

        if (!string.IsNullOrWhiteSpace(query.Channel))
        {
            var channel = query.Channel.Trim().ToLower();

            // Igualdade case-insensitive: "linx" encontra "Linx" (T074).
            scoped = scoped.Where(o => o.Channel != null && o.Channel.ToLower() == channel);
        }

        if (query.CustomerId is { } customerId)
        {
            scoped = scoped.Where(o => o.CustomerId == customerId);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLower();

            // Busca no número do pedido OU no nome do cliente (FR-017); a referência a
            // o.Customer traduz para LEFT JOIN na consulta emitida.
            scoped = scoped.Where(o => o.OrderNumber.ToLower().Contains(term)
                                       || (o.Customer != null && o.Customer.Name.ToLower().Contains(term)));
        }

        var totalCount = await scoped.CountAsync();

        var items = await scoped
            .Include(o => o.Customer)
            .Include(o => o.Items)
            .OrderByDescending(o => o.OrderDate)
            .ThenByDescending(o => o.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync();

        foreach (var order in items)
        {
            SortItems(order);
        }

        return (items, totalCount);
    }

    /// <inheritdoc />
    public async Task<Order> InsertAsync(Order entity)
    {
        // As navegações servem apenas para montar o DTO da resposta. Sem fixar o estado, a
        // travessia de grafo de Add() trataria o cliente (carregado com AsNoTracking, portanto
        // fora do change tracker) como uma entidade nova e emitiria INSERT em customers, o que o
        // PostgreSQL recusa com 428C9 — a PK é GENERATED ALWAYS. O provedor InMemory dos testes
        // de repositório não impõe essa regra, então ela é resolvida aqui.
        AttachAsExisting(entity.Customer);
        AttachAsExisting(entity.User);

        _context.Orders.Add(entity);

        try
        {
            // Pedido e itens numa única SaveChangesAsync: a transação implícita do EF escreve os
            // dois juntos (FR-018), sem deixar item órfão.
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            throw new DuplicateException("Já existe um pedido com este número, data e tipo.");
        }

        return entity;
    }

    /// <inheritdoc />
    public async Task<Order> UpdateAsync(Order entity)
    {
        var existing = await _context.Orders
            .FirstOrDefaultAsync(o => o.Id == entity.Id && o.UserId == entity.UserId);

        if (existing is null)
        {
            throw new NotFoundException("Pedido não encontrado.");
        }

        // Só os escalares: a atualização de status/total (FR-019) não toca as linhas.
        _context.Entry(existing).CurrentValues.SetValues(entity);
        await _context.SaveChangesAsync();

        return existing;
    }

    private static void SortItems(Order order) =>
        // As linhas aparecem numeradas na ordem, independentemente da ordem retornada pelo banco.
        order.Items = [.. order.Items.OrderBy(i => i.LineNo)];

    private void AttachAsExisting<TEntity>(TEntity? entity) where TEntity : class
    {
        if (entity is null)
        {
            return;
        }

        var tracked = _context.Entry(entity);

        if (tracked.State == EntityState.Detached)
        {
            tracked.State = EntityState.Unchanged;
        }
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: UniqueViolation };
}
