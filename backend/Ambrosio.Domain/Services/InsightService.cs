using Ambrosio.Domain.Common;
using Ambrosio.Domain.Exceptions;
using Ambrosio.Domain.Interfaces;
using Ambrosio.Domain.Models;
using Ambrosio.Domain.ValueObjects;
using Ambrosio.DTO.Customers;
using Ambrosio.DTO.Enums;
using Ambrosio.DTO.Insights;
using Ambrosio.Infra.Interfaces.AppServices;
using Ambrosio.Infra.Interfaces.Repository;
using FluentValidation;
using FluentValidation.Results;

namespace Ambrosio.Domain.Services;

/// <summary>
/// Consultas prontas do plugin (feature 007, T051). As regras de negócio são as do doc 02: líquido =
/// soma dos totais (devoluções e cancelamentos já negativos), atendimentos = vendas, PA = peças /
/// atendimentos, VA = líquido / atendimentos, e o mês de cota fecha no dia 25.
/// </summary>
public class InsightService : IInsightService
{
    /// <summary>Teto de qualquer lista desta classe (FR-016).</summary>
    public const int MaxRows = 200;

    /// <summary>Último dia do mês de cota; o seguinte começa no dia 26.</summary>
    public const int QuotaClosingDay = 25;

    private const string CustomerNotFound = "Cliente não encontrado.";

    private readonly ICustomerRepository<Customer> _customers;
    private readonly IOrderRepository<Order> _orders;
    private readonly ICpfProtectionAppService _cpfProtection;
    private readonly IClock _clock;

    public InsightService(
        ICustomerRepository<Customer> customers,
        IOrderRepository<Order> orders,
        ICpfProtectionAppService cpfProtection,
        IClock clock)
    {
        _customers = customers;
        _orders = orders;
        _cpfProtection = cpfProtection;
        _clock = clock;
    }

    public async Task<InactiveCustomersResult> ListInactiveCustomersAsync(long userId, InactiveCustomersQuery query)
    {
        var errors = new List<ValidationFailure>();

        if (query.Days < 1)
        {
            errors.Add(new ValidationFailure("days", "O número de dias deve ser maior ou igual a 1."));
        }

        if (query.MinPurchases is < 0)
        {
            errors.Add(new ValidationFailure("minPurchases", "O mínimo de compras não pode ser negativo."));
        }

        if (query.Limit is < 1 or > MaxRows)
        {
            errors.Add(new ValidationFailure("limit", $"O limite deve estar entre 1 e {MaxRows}."));
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        var today = SaoPauloCalendar.Today(_clock);
        var (items, totalCount) = await _customers.ListInactiveAsync(
            userId, today.AddDays(-query.Days), query.MinPurchases, query.MinTotal, query.Limit, today);

        return new InactiveCustomersResult { Items = items, TotalCount = totalCount };
    }

    public async Task<PeriodSummaryInfo> GetPeriodSummaryAsync(long userId, PeriodSummaryQuery query)
    {
        var (from, to) = ResolvePeriod(query.From, query.To);
        var (netTotal, sales, pieces) = await _orders.GetPeriodSummaryAsync(userId, from, to);

        return new PeriodSummaryInfo
        {
            From = from,
            To = to,
            NetTotal = netTotal,
            Sales = sales,
            Pieces = pieces,
            Pa = sales > 0 ? Math.Round((decimal)pieces / sales, 2) : null,
            Va = sales > 0 ? Math.Round(netTotal / sales, 2) : null
        };
    }

    public async Task<ReturnsCancellationsInfo> GetReturnsCancellationsAsync(long userId, DateOnly from, DateOnly to)
    {
        EnsureOrdered(from, to);

        var totals = await _orders.GetReturnsCancellationsAsync(userId, from, to);

        return new ReturnsCancellationsInfo
        {
            From = from,
            To = to,
            Returns = totals.GetValueOrDefault(OrderKind.Return) ?? new KindTotalsInfo(),
            Cancellations = totals.GetValueOrDefault(OrderKind.Cancellation) ?? new KindTotalsInfo()
        };
    }

    public async Task<CustomerInfo?> FindCustomerByCpfAsync(long userId, string cpf)
    {
        if (!Cpf.TryParse(cpf, out var parsed) || parsed is null)
        {
            throw new ValidationException([new ValidationFailure("cpf", "CPF inválido.")]);
        }

        var customer = await _customers.GetByCpfHashAsync(userId, _cpfProtection.Hash(parsed.Digits));

        return customer is null
            ? null
            : new CustomerInfo
            {
                Id = customer.Id,
                Name = customer.Name,
                CpfMasked = customer.CpfMasked,
                CreatedAt = customer.CreatedAt,
                UpdatedAt = customer.UpdatedAt
            };
    }

    public async Task<CustomerHistoryInfo> GetCustomerHistoryAsync(long userId, long customerId, int limit)
    {
        if (limit is < 1 or > MaxRows)
        {
            throw new ValidationException([new ValidationFailure("limit", $"O limite deve estar entre 1 e {MaxRows}.")]);
        }

        var customer = await _customers.GetByIdAsync(customerId, userId)
            ?? throw new NotFoundException(CustomerNotFound);

        var summary = await _customers.GetSummaryAsync(customerId, userId, SaoPauloCalendar.Today(_clock));
        var (orders, totalOrders) = await _orders.ListByCustomerWithItemsAsync(userId, customerId, limit);

        return new CustomerHistoryInfo
        {
            Customer = new CustomerInfo
            {
                Id = customer.Id,
                Name = customer.Name,
                CpfMasked = customer.CpfMasked,
                CreatedAt = customer.CreatedAt,
                UpdatedAt = customer.UpdatedAt
            },
            Summary = summary,
            AverageTicket = summary.Purchases > 0 ? Math.Round(summary.NetTotal / summary.Purchases, 2) : null,
            Orders = orders.Select(OrderService.MapToDetail).ToList(),
            TotalOrders = totalOrders
        };
    }

    public (DateOnly From, DateOnly To) QuotaMonth(DateOnly day)
    {
        // Do dia 26 ao 25: depois do fechamento, o mês de cota já é o seguinte.
        var closingMonth = day.Day > QuotaClosingDay ? day.AddMonths(1) : day;
        var to = new DateOnly(closingMonth.Year, closingMonth.Month, QuotaClosingDay);

        return (to.AddMonths(-1).AddDays(1), to);
    }

    /// <summary>
    /// Sem datas: mês de cota atual. Com só uma: a outra ponta é o mesmo mês de cota dela.
    /// </summary>
    private (DateOnly From, DateOnly To) ResolvePeriod(DateOnly? from, DateOnly? to)
    {
        if (from is null && to is null)
        {
            return QuotaMonth(SaoPauloCalendar.Today(_clock));
        }

        var resolvedFrom = from ?? QuotaMonth(to!.Value).From;
        var resolvedTo = to ?? QuotaMonth(from!.Value).To;

        EnsureOrdered(resolvedFrom, resolvedTo);

        return (resolvedFrom, resolvedTo);
    }

    private static void EnsureOrdered(DateOnly from, DateOnly to)
    {
        if (from > to)
        {
            throw new ValidationException([new ValidationFailure("from", "A data inicial não pode ser maior que a data final.")]);
        }
    }
}
