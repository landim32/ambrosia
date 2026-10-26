using Ambrosia.Domain.Exceptions;
using Ambrosia.Domain.Models;
using Ambrosia.DTO.Customers;
using Ambrosia.DTO.Enums;
using Ambrosia.Infra.Context;
using Ambrosia.Infra.Interfaces.Repository;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ambrosia.Infra.Repository;

/// <summary>
/// Acesso à tabela <c>customers</c> com isolamento estrutural (FR-021, research §13): cada
/// consulta de leitura começa por <c>Where(c => c.UserId == userId)</c>, inclusive a agregação do
/// resumo de compras. Leituras usam <c>AsNoTracking</c>.
/// </summary>
public class CustomerRepository : ICustomerRepository<Customer>
{
    /// <summary>SQLSTATE de violação de unicidade no PostgreSQL (research §11).</summary>
    private const string UniqueViolation = "23505";

    private readonly AmbrosiaContext _context;

    public CustomerRepository(AmbrosiaContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task<Customer?> GetByIdAsync(long customerId, long userId)
    {
        return await _context.Customers
            .AsNoTracking()
            .Where(c => c.UserId == userId)
            .FirstOrDefaultAsync(c => c.Id == customerId);
    }

    /// <inheritdoc />
    public async Task<bool> ExistsByCpfHashAsync(long userId, string cpfHash)
    {
        return await _context.Customers
            .AsNoTracking()
            .Where(c => c.UserId == userId)
            .AnyAsync(c => c.CpfHash == cpfHash);
    }

    /// <inheritdoc />
    public async Task<(List<Customer> Items, int TotalCount)> ListAsync(
        long userId, string? search, int page, int pageSize)
    {
        // O índice ix_customers_user_name (user_id, name) atende o filtro + a ordenação (SC-003).
        var query = _context.Customers
            .AsNoTracking()
            .Where(c => c.UserId == userId)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();

            // Npgsql traduz para lower(name) LIKE lower('%term%'): case-insensitive como exige a
            // FR-008, e compatível com o provedor InMemory dos testes (tasks.md, ajuste ao §9).
            query = query.Where(c => c.Name.ToLower().Contains(term));
        }

        var totalCount = await query.CountAsync();

        var items = await query
            .OrderBy(c => c.Name)
            .ThenBy(c => c.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    /// <inheritdoc />
    public async Task<Customer> InsertAsync(Customer entity)
    {
        _context.Customers.Add(entity);

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Corrida entre duas inserções do mesmo (user_id, cpf_hash): ux_customers_user_cpf
            // decide o vencedor (research §11).
            throw new DuplicateException("Já existe um cliente com este CPF.");
        }

        return entity;
    }

    /// <inheritdoc />
    public async Task<Customer> UpdateAsync(Customer entity)
    {
        var existing = await _context.Customers
            .FirstOrDefaultAsync(c => c.Id == entity.Id && c.UserId == entity.UserId);

        if (existing is null)
        {
            throw new NotFoundException("Cliente não encontrado.");
        }

        _context.Entry(existing).CurrentValues.SetValues(entity);
        await _context.SaveChangesAsync();

        return existing;
    }

    /// <inheritdoc />
    public async Task<CustomerSummaryInfo> GetSummaryAsync(
        long customerId, long userId, DateOnly today)
    {
        // Pedidos do cliente DO usuário: o filtro por user_id vem primeiro e também neutraliza o
        // customer_id repetido para outro usuário (SC-002). Todas as três leituras caem no índice
        // ix_orders_user_customer (user_id, customer_id).
        var owned = _context.Orders
            .AsNoTracking()
            .Where(o => o.UserId == userId && o.CustomerId == customerId);

        var purchases = await owned.CountAsync(o => o.Kind == OrderKind.Sale);

        // (decimal?) evita o SUM()=NULL do SQL em conjunto vazio: 0 é o valor de resumo (FR-009).
        var netTotal = await owned.SumAsync(o => (decimal?)o.Total) ?? 0m;

        var lastPurchase = await owned
            .Where(o => o.Kind == OrderKind.Sale)
            .MaxAsync(o => (DateOnly?)o.OrderDate);

        return new CustomerSummaryInfo
        {
            Purchases = purchases,
            NetTotal = netTotal,
            LastPurchase = lastPurchase,
            // Dias calculados em memória (research §10): a subtração de datas não é traduzida.
            DaysSinceLastPurchase = lastPurchase is { } sale
                ? today.DayNumber - sale.DayNumber
                : null
        };
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: UniqueViolation };
}
