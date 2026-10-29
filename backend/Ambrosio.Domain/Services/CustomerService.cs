using Ambrosio.Domain.Exceptions;
using Ambrosio.Domain.Interfaces;
using Ambrosio.Domain.Models;
using Ambrosio.Domain.ValueObjects;
using Ambrosio.DTO.Common;
using Ambrosio.DTO.CustomerPhotos;
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

    /// <summary>
    /// Avatar das telas (feature 009, FR-016). O cliente não sabe montar URL de storage nem assinar
    /// nada: quem conhece o álbum é este serviço, e a foto de perfil é uma coluna de <c>customers</c>
    /// lida em lote (research R5/R10). Sem ciclo de dependência: <see cref="CustomerPhotoService"/>
    /// depende do repositório de clientes, nunca deste serviço.
    /// </summary>
    private readonly ICustomerPhotoService _photos;

    private readonly IValidator<CustomerInsertInfo> _insertValidator;
    private readonly IValidator<CustomerUpdateInfo> _updateValidator;
    private readonly IValidator<CustomerListQuery> _listValidator;
    private readonly IClock _clock;

    public CustomerService(
        ICustomerRepository<Customer> customers,
        ICpfProtectionAppService cpfProtection,
        ICustomerPhotoService photos,
        IValidator<CustomerInsertInfo> insertValidator,
        IValidator<CustomerUpdateInfo> updateValidator,
        IValidator<CustomerListQuery> listValidator,
        IClock clock)
    {
        _customers = customers;
        _cpfProtection = cpfProtection;
        _photos = photos;
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

        // Um lote para a página inteira: resolver o avatar cliente a cliente seria um N+1 em toda
        // listagem, e é justamente a tela onde o círculo existe (T042).
        var profiles = await _photos.GetProfilesAsync(
            userId, items.Select(c => c.Id).ToList(), includeOriginalUrl: false);

        return new PagedResult<CustomerInfo>
        {
            // CustomerInfo não tem propriedade para o CPF completo: impossível vazar por aqui.
            Items = items.Select(customer => MapToInfo(customer, profiles.GetValueOrDefault(customer.Id))).ToList(),
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
        var (cpf, cpfHash) = await ValidateNewCustomerAsync(userId, info);

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

        // Álbum vazio por construção: sem leitura de fotos na criação (MapToDetailAsync, `recemCriada`).
        return await MapToDetailAsync(saved, userId, recemCriada: true);
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

    private async Task<CustomerDetailInfo> MapToDetailAsync(
        Customer customer, long userId, bool recemCriada = false)
    {
        var summary = await _customers.GetSummaryAsync(customer.Id, userId, _clock.Today);

        // No detalhe o avatar vem com `originalUrl`: é aqui que o toque no círculo abre a foto
        // inteira (FR-014, cenário US3-4).
        //
        // `recemCriada` pula as duas leituras do álbum: uma cliente que acabou de nascer não tem foto
        // nenhuma por construção (nenhuma linha em `customer_photos` aponta para um id que surgiu agora),
        // e consultar mesmo assim acrescentava duas idas ao banco a todo `POST /customers` — caminho que
        // a suíte de leitura exerce em massa — para devolver um número que só pode ser zero.
        CustomerProfilePhotoInfo? profilePhoto = null;
        var photoCount = 0;

        if (!recemCriada)
        {
            var profiles = await _photos.GetProfilesAsync(userId, [customer.Id], includeOriginalUrl: true);

            profilePhoto = profiles.GetValueOrDefault(customer.Id);
            photoCount = await _photos.CountAsync(userId, customer.Id);
        }

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
            Summary = summary,
            ProfilePhoto = profilePhoto,
            PhotoCount = photoCount
        };
    }

    /// <inheritdoc />
    public async Task<string> ValidateCreateAsync(long userId, CustomerInsertInfo info)
    {
        var (cpf, _) = await ValidateNewCustomerAsync(userId, info);

        return cpf.Masked;
    }

    /// <summary>Validator, normalização do CPF e duplicidade — compartilhado pela gravação e pela prévia.</summary>
    private async Task<(Cpf Cpf, string CpfHash)> ValidateNewCustomerAsync(long userId, CustomerInsertInfo info)
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

        return (cpf, cpfHash);
    }

    private static CustomerInfo MapToInfo(
        Customer customer, CustomerProfilePhotoInfo? profilePhoto = null) => new()
    {
        Id = customer.Id,
        Name = customer.Name,
        CpfMasked = customer.CpfMasked,
        CreatedAt = customer.CreatedAt,
        UpdatedAt = customer.UpdatedAt,
        ProfilePhoto = profilePhoto
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
