using Ambrosio.DTO.Customers;

namespace Ambrosio.Infra.Interfaces.Repository;

/// <summary>
/// Persistência de clientes (tabela <c>customers</c>).
/// <para>
/// Isolamento estrutural (FR-021, research §13): todo método de <b>leitura</b> exige
/// <c>userId</c> e a implementação aplica <c>Where(c => c.UserId == userId)</c> como primeiro
/// filtro — não existe consulta "sem filtro" para esquecer de usar. Escrita recebe a entidade já
/// obtida por leitura, portanto já pertencente ao usuário.
/// </para>
/// </summary>
/// <typeparam name="TModel">Modelo do cliente.</typeparam>
public interface ICustomerRepository<TModel> where TModel : class
{
    /// <summary>Cliente pelo id, apenas se for do usuário; <c>null</c> nos dois casos (FR-022).</summary>
    Task<TModel?> GetByIdAsync(long customerId, long userId);

    /// <summary>Se já existe cliente com este hash de CPF <b>para o usuário</b> (FR-010).</summary>
    Task<bool> ExistsByCpfHashAsync(long userId, string cpfHash);

    /// <summary>Busca paginada por nome, ordenada por nome (FR-008).</summary>
    Task<(List<TModel> Items, int TotalCount)> ListAsync(long userId, string? search, int page, int pageSize);

    Task<TModel> InsertAsync(TModel entity);

    Task<TModel> UpdateAsync(TModel entity);

    /// <summary>
    /// Resumo de compras do cliente (FR-009, research §10), agregado sobre <c>orders</c> do
    /// próprio usuário.
    /// </summary>
    /// <param name="today">Data usada em <c>daysSinceLastPurchase</c> (vem de <c>IClock</c>, para ser testável).</param>
    Task<CustomerSummaryInfo> GetSummaryAsync(long customerId, long userId, DateOnly today);
}
