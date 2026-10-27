using Ambrosio.Domain.Exceptions;
using Ambrosio.Domain.Interfaces;
using Ambrosio.Domain.Models;
using Ambrosio.Domain.ValueObjects;
using Ambrosio.DTO.Common;
using Ambrosio.DTO.Customers;
using Ambrosio.Infra.Interfaces.AppServices;
using Ambrosio.Infra.Interfaces.Repository;
using FluentValidation;
using FluentValidation.Results;

namespace Ambrosio.Domain.Services;

/// <summary>
/// Regras de cliente (FR-006 a FR-011, FR-021, FR-022). A proteção do CPF fica aqui em cima da
/// abstração da Infra: o serviço decide que <c>cpf_hash</c> é calculado sobre os dígitos
/// canônicos, que <c>cpf_encrypted</c> só é lido no detalhe e que nenhuma listagem carrega o
/// documento completo.
/// </summary>
public class CustomerService : ICustomerService
{
    /// <summary>Campo citado no erro de CPF (FR-024).</summary>
    private const string CPF_FIELD = "cpf";

    private const string CustomerNotFound = "Cliente não encontrado.";

    private readonly ICustomerRepository<Customer> _customers;
    private readonly ICpfProtectionAppService _cpfProtection;
    private readonly IValidator<CustomerInsertInfo> _insertValidator;
    private readonly IValidator<CustomerUpdateInfo> _updateValidator;
    private readonly IValidator<CustomerListQuery> _listValidator;
    private readonly IClock _clock;

    public CustomerService(
        ICustomerRepository<Customer> customers,
        ICpfProtectionAppService cpfProtection,
        IValidator<CustomerInsertInfo> insertValidator,
        IValidator<CustomerUpdateInfo> updateValidator,
        IValidator<CustomerListQuery> listValidator,
        IClock clock)
    {
        _customers = customers;
        _cpfProtection = cpfProtection;
        _insertValidator = insertValidator;
        _updateValidator = updateValidator;
        _listValidator = listValidator;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<PagedResult<CustomerInfo>> ListAsync(long userId, CustomerListQuery query)
    {
        await ThrowIfInvalid(_listValidator, query);

        var (items, totalCount) = await _customers.ListAsync(
            userId, query.Search, query.Page, query.PageSize);

        return new PagedResult<CustomerInfo>
        {
            // CustomerInfo não tem propriedade para o CPF completo: impossível vazar por aqui.
            Items = items.Select(MapToInfo).ToList(),
            Page = query.Page,
            PageSize = query.PageSize,
            TotalCount = totalCount
        };
    }

    /// <inheritdoc />
    public async Task<CustomerDetailInfo> GetByIdAsync(long userId, long customerId)
    {
        var customer = await GetOwnedOrThrowAsync(customerId, userId);

        return await MapToDetailAsync(customer, userId);
    }

    /// <inheritdoc />
    public async Task<CustomerDetailInfo> CreateAsync(long userId, CustomerInsertInfo info)
    {
        await ThrowIfInvalid(_insertValidator, info);

        // O validator já recusou CPF inválido; aqui o TryParse normaliza (descarta pontuação) e
        // entrega os dígitos canônicos, única forma aceita no hash (FR-010, research §5).
        if (!Cpf.TryParse(info.Cpf, out var cpf) || cpf is null)
        {
            throw new ValidationException([new ValidationFailure(CPF_FIELD, "CPF inválido.")]);
        }

        var cpfHash = _cpfProtection.Hash(cpf.Digits);

        if (await _customers.ExistsByCpfHashAsync(userId, cpfHash))
        {
            throw new DuplicateException("Já existe um cliente com este CPF.");
        }

        var now = _clock.UtcNow;
        var customer = new Customer
        {
            UserId = userId,
            Name = info.Name.Trim(),
            CpfHash = cpfHash,
            CpfEncrypted = _cpfProtection.Encrypt(cpf.Digits),
            CpfMasked = cpf.Masked,
            CreatedAt = now,
            UpdatedAt = now
        };

        var saved = await _customers.InsertAsync(customer);

        return await MapToDetailAsync(saved, userId);
    }

    /// <inheritdoc />
    public async Task<CustomerDetailInfo> UpdateAsync(long userId, long customerId, CustomerUpdateInfo info)
    {
        await ThrowIfInvalid(_updateValidator, info);

        var customer = await GetOwnedOrThrowAsync(customerId, userId);

        customer.UpdateName(info.Name, _clock.UtcNow);

        var saved = await _customers.UpdateAsync(customer);

        return await MapToDetailAsync(saved, userId);
    }

    /// <summary>
    /// Inexistente e "de outro usuário" produzem exatamente a mesma resposta (FR-022, SC-002):
    /// o 404 não revela se o recurso existe.
    /// </summary>
    private async Task<Customer> GetOwnedOrThrowAsync(long customerId, long userId)
    {
        var customer = await _customers.GetByIdAsync(customerId, userId);

        return customer ?? throw new NotFoundException(CustomerNotFound);
    }

    private async Task<CustomerDetailInfo> MapToDetailAsync(Customer customer, long userId)
    {
        var summary = await _customers.GetSummaryAsync(customer.Id, userId, _clock.Today);

        return new CustomerDetailInfo
        {
            Id = customer.Id,
            Name = customer.Name,
            CpfMasked = customer.CpfMasked,
            CreatedAt = customer.CreatedAt,
            UpdatedAt = customer.UpdatedAt,
            // Único ponto que decifra: a listagem usa MapToInfo e não chega aqui (SC-005).
            Cpf = Cpf.TryParse(_cpfProtection.Decrypt(customer.CpfEncrypted), out var parsed)
                ? parsed!.Formatted
                : customer.CpfMasked,
            Summary = summary
        };
    }

    private static CustomerInfo MapToInfo(Customer customer) => new()
    {
        Id = customer.Id,
        Name = customer.Name,
        CpfMasked = customer.CpfMasked,
        CreatedAt = customer.CreatedAt,
        UpdatedAt = customer.UpdatedAt
    };

    private static async Task ThrowIfInvalid<T>(IValidator<T> validator, T target)
    {
        var result = await validator.ValidateAsync(target);

        if (!result.IsValid)
        {
            throw new ValidationException(result.Errors);
        }
    }
}
