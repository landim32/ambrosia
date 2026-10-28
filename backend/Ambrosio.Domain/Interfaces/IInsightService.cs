using Ambrosio.DTO.Customers;
using Ambrosio.DTO.Insights;

namespace Ambrosio.Domain.Interfaces;

/// <summary>
/// Consultas prontas do plugin do ChatGPT (feature 007, T051, data-model §4): sem endpoint
/// equivalente na API, todas filtradas pelo usuário e calculadas no banco.
/// </summary>
public interface IInsightService
{
    /// <exception cref="FluentValidation.ValidationException">Filtros fora do intervalo.</exception>
    Task<InactiveCustomersResult> ListInactiveCustomersAsync(long userId, InactiveCustomersQuery query);

    /// <summary>Sem datas, vale o mês de cota atual (26 a 25, horário de São Paulo).</summary>
    Task<PeriodSummaryInfo> GetPeriodSummaryAsync(long userId, PeriodSummaryQuery query);

    Task<ReturnsCancellationsInfo> GetReturnsCancellationsAsync(long userId, DateOnly from, DateOnly to);

    /// <summary>Cliente pelo CPF (normalizado e buscado pelo hash); <c>null</c> se não houver.</summary>
    /// <exception cref="FluentValidation.ValidationException">CPF inválido.</exception>
    Task<CustomerInfo?> FindCustomerByCpfAsync(long userId, string cpf);

    /// <exception cref="Exceptions.NotFoundException">Cliente inexistente ou de outro usuário.</exception>
    Task<CustomerHistoryInfo> GetCustomerHistoryAsync(long userId, long customerId, int limit);

    /// <summary>Mês de cota que contém <paramref name="day"/>: do dia 26 anterior ao dia 25.</summary>
    (DateOnly From, DateOnly To) QuotaMonth(DateOnly day);
}
