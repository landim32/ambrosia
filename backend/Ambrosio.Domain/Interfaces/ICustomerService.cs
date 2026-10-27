using Ambrosio.DTO.Common;
using Ambrosio.DTO.Customers;

namespace Ambrosio.Domain.Interfaces;

/// <summary>
/// Cadastro e consulta de clientes da vendedora autenticada (FR-006 a FR-011). Todo método recebe
/// o <c>userId</c> do token: o isolamento é parâmetro obrigatório, não convenção (research §13).
/// </summary>
public interface ICustomerService
{
    /// <summary>Busca paginada por nome, com CPF mascarado (FR-008).</summary>
    Task<PagedResult<CustomerInfo>> ListAsync(long userId, CustomerListQuery query);

    /// <summary>Detalhe com CPF completo e resumo de compras (FR-009).</summary>
    Task<CustomerDetailInfo> GetByIdAsync(long userId, long customerId);

    /// <summary>Cadastro do cliente com CPF protegido (FR-006, FR-007, FR-010, FR-011).</summary>
    Task<CustomerDetailInfo> CreateAsync(long userId, CustomerInsertInfo info);

    /// <summary>Atualização do nome (FR-009).</summary>
    Task<CustomerDetailInfo> UpdateAsync(long userId, long customerId, CustomerUpdateInfo info);
}
