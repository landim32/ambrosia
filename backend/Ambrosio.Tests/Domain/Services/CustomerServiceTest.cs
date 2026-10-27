using Ambrosio.Domain.Exceptions;
using Ambrosio.Domain.Interfaces;
using Ambrosio.Domain.Models;
using Ambrosio.Domain.Services;
using Ambrosio.Domain.Validators;
using Ambrosio.DTO.Common;
using Ambrosio.DTO.Customers;
using Ambrosio.Infra.Interfaces.AppServices;
using Ambrosio.Infra.Interfaces.Repository;
using Ambrosio.Tests.TestHelpers;
using FluentValidation;
using Moq;

namespace Ambrosio.Tests.Domain.Services;

/// <summary>
/// T054 [US2]: regras de cadastro/consulta de cliente (FR-006 a FR-011, FR-021, FR-022) —
/// proteção do CPF, unicidade por usuário, isolamento e mapeamento que nunca expõe o CPF
/// completo em listagem.
/// </summary>
public class CustomerServiceTest
{
    private const long USER_ID = 7;
    private const string CPF_RAW = "529.982.247-25";
    private const string CPF_DIGITS = "52998224725";
    private const string CPF_MASKED = "***.982.247-**";
    private const string CPF_HASH = "hash-hmac-64-chars";
    private const string CPF_ENCRYPTED = "cifra-base64";

    private static readonly DateTime NOW = new(2026, 10, 25, 12, 0, 0, DateTimeKind.Unspecified);

    private readonly Mock<ICustomerRepository<Customer>> _repository = new();
    private readonly Mock<ICpfProtectionAppService> _cpfProtection = new();
    private readonly FakeClock _clock = new(NOW);
    private readonly CustomerService _sut;

    public CustomerServiceTest()
    {
        _cpfProtection.Setup(p => p.Hash(CPF_DIGITS)).Returns(CPF_HASH);
        _cpfProtection.Setup(p => p.Encrypt(CPF_DIGITS)).Returns(CPF_ENCRYPTED);
        _cpfProtection.Setup(p => p.Decrypt(CPF_ENCRYPTED)).Returns(CPF_DIGITS);

        _repository.Setup(r => r.ExistsByCpfHashAsync(It.IsAny<long>(), It.IsAny<string>()))
            .ReturnsAsync(false);
        _repository.Setup(r => r.InsertAsync(It.IsAny<Customer>())).ReturnsAsync((Customer c) => c);
        _repository.Setup(r => r.UpdateAsync(It.IsAny<Customer>())).ReturnsAsync((Customer c) => c);
        _repository.Setup(r => r.GetSummaryAsync(It.IsAny<long>(), It.IsAny<long>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(new CustomerSummaryInfo());

        // Validators reais: as regras de formato são da própria história e o serviço é testado de
        // ponta a ponta (T054). Os mocks ficam nas fronteiras (repositório, AppService, relógio).
        _sut = new CustomerService(
            _repository.Object,
            _cpfProtection.Object,
            new CustomerInsertInfoValidator(),
            new CustomerUpdateInfoValidator(),
            new CustomerListQueryValidator(),
            _clock);
    }

    private static Customer NewCustomer(long id = 11, long userId = USER_ID) => new()
    {
        Id = id,
        UserId = userId,
        Name = "Maria Silva",
        CpfHash = CPF_HASH,
        CpfEncrypted = CPF_ENCRYPTED,
        CpfMasked = CPF_MASKED,
        CreatedAt = NOW,
        UpdatedAt = NOW
    };

    private static CustomerInsertInfo Insert(string name = "Maria Silva", string cpf = CPF_RAW)
        => new() { Name = name, Cpf = cpf };

    [Fact]
    public async Task CreateAsync_ShouldNormalizeAndProtectCpf_WhenCpfIsValid()
    {
        // Act: FR-006 aceita qualquer formatação; FR-011 grava hash + cifra + máscara.
        var result = await _sut.CreateAsync(USER_ID, Insert(name: "  Maria Silva  "));

        // Assert
        _repository.Verify(r => r.InsertAsync(It.Is<Customer>(c =>
            c.UserId == USER_ID
            && c.Name == "Maria Silva"
            && c.CpfHash == CPF_HASH
            && c.CpfEncrypted == CPF_ENCRYPTED
            && c.CpfMasked == CPF_MASKED
            && c.CreatedAt == NOW
            && c.UpdatedAt == NOW)), Times.Once);

        Assert.Equal(CPF_MASKED, result.CpfMasked);
    }

    [Fact]
    public async Task CreateAsync_ShouldHashTheCanonicalDigits_WhenCpfHasPunctuation()
    {
        // Act: "529.982.247-25" e "52998224725" precisam produzir o mesmo hash (FR-010).
        await _sut.CreateAsync(USER_ID, Insert(cpf: CPF_DIGITS));

        // Assert
        _cpfProtection.Verify(p => p.Hash(CPF_DIGITS), Times.Once);
        _cpfProtection.Verify(p => p.Hash(It.Is<string>(s => s != CPF_DIGITS)), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_ShouldThrowDuplicate_WhenCpfAlreadyRegisteredForTheUser()
    {
        // Arrange: FR-010 — duplicidade é sempre por (usuário, CPF).
        _repository.Setup(r => r.ExistsByCpfHashAsync(USER_ID, CPF_HASH)).ReturnsAsync(true);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<DuplicateException>(() => _sut.CreateAsync(USER_ID, Insert()));

        Assert.Equal("Já existe um cliente com este CPF.", ex.Message);
        _repository.Verify(r => r.InsertAsync(It.IsAny<Customer>()), Times.Never);
    }

    [Theory]
    [InlineData("111.111.111-11")]
    [InlineData("529.982.247-26")]
    [InlineData("5299822472")]
    [InlineData("")]
    [InlineData(null)]
    public async Task CreateAsync_ShouldThrowValidation_WhenCpfIsInvalid(string? cpf)
    {
        // Act & Assert: FR-007 bloqueia antes de qualquer escrita.
        await Assert.ThrowsAsync<ValidationException>(
            () => _sut.CreateAsync(USER_ID, Insert(cpf: cpf!)));

        _repository.Verify(r => r.InsertAsync(It.IsAny<Customer>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_ShouldThrowValidation_WhenNameIsTooShort()
    {
        // Act & Assert: FR-006, 2-200 caracteres (data-model).
        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => _sut.CreateAsync(USER_ID, Insert(name: "M")));

        Assert.Equal("Name", ex.Errors.Single().PropertyName);
    }

    [Fact]
    public async Task GetByIdAsync_ShouldDecryptAndFormatCpf_WithSummary()
    {
        // Arrange: FR-009 — só o detalhe devolve o CPF completo.
        _repository.Setup(r => r.GetByIdAsync(11, USER_ID)).ReturnsAsync(NewCustomer());
        _repository.Setup(r => r.GetSummaryAsync(11, USER_ID, It.IsAny<DateOnly>()))
            .ReturnsAsync(new CustomerSummaryInfo
            {
                Purchases = 3,
                NetTotal = 1250.50m,
                LastPurchase = new DateOnly(2026, 10, 20),
                DaysSinceLastPurchase = 5
            });

        // Act
        var result = await _sut.GetByIdAsync(USER_ID, customerId: 11);

        // Assert
        Assert.Equal("529.982.247-25", result.Cpf);
        Assert.Equal(CPF_MASKED, result.CpfMasked);
        Assert.Equal(3, result.Summary.Purchases);
        Assert.Equal(1250.50m, result.Summary.NetTotal);
        Assert.Equal(5, result.Summary.DaysSinceLastPurchase);

        _repository.Verify(r => r.GetSummaryAsync(11, USER_ID, new DateOnly(2026, 10, 25)), Times.Once);
    }

    [Fact]
    public async Task GetByIdAsync_ShouldThrowNotFound_WhenRepositoryReturnsNull()
    {
        // Arrange: null cobre os dois casos — inexistente e de outro usuário (FR-022, SC-002) —
        // e a mensagem precisa ser idêntica para não revelar a existência do recurso.
        _repository.Setup(r => r.GetByIdAsync(It.IsAny<long>(), It.IsAny<long>())).ReturnsAsync((Customer?)null);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetByIdAsync(USER_ID, 999));

        Assert.Equal("Cliente não encontrado.", ex.Message);
    }

    [Fact]
    public async Task ListAsync_ShouldReturnOnlyMaskedCpf()
    {
        // Arrange: SC-005 — a listagem nunca carrega o CPF completo.
        _repository.Setup(r => r.ListAsync(USER_ID, "mar", 1, 20))
            .ReturnsAsync((new List<Customer> { NewCustomer() }, 1));

        // Act
        PagedResult<CustomerInfo> result = await _sut.ListAsync(USER_ID,
            new CustomerListQuery { Search = "mar", Page = 1, PageSize = 20 });

        // Assert
        var item = Assert.Single(result.Items);
        Assert.Equal(CPF_MASKED, item.CpfMasked);

        // O tipo de listagem não tem campo algum para o CPF completo.
        Assert.Null(typeof(CustomerInfo).GetProperty("Cpf"));

        Assert.Equal(1, result.TotalCount);
        Assert.Equal(1, result.Page);
        Assert.Equal(20, result.PageSize);
    }

    [Fact]
    public async Task ListAsync_ShouldPassUserIdToTheRepository()
    {
        // FR-021: o isolamento começa no serviço, que repassa o userId do token.
        _repository
            .Setup(r => r.ListAsync(It.IsAny<long>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync((new List<Customer>(), 0));

        await _sut.ListAsync(USER_ID, new CustomerListQuery { Search = "Ana" });

        _repository.Verify(r => r.ListAsync(USER_ID, "Ana", 1, 20), Times.Once);
    }

    [Fact]
    public async Task ListAsync_ShouldThrowValidation_WhenPageSizeIsOverTheLimit()
    {
        // FR-008 / research §9: pageSize limitado a 100.
        await Assert.ThrowsAsync<ValidationException>(
            () => _sut.ListAsync(USER_ID, new CustomerListQuery { PageSize = 500 }));
    }

    [Fact]
    public async Task UpdateAsync_ShouldChangeNameAndUpdatedAt()
    {
        // Arrange: FR-009.
        _repository.Setup(r => r.GetByIdAsync(11, USER_ID)).ReturnsAsync(NewCustomer());

        // Act
        var result = await _sut.UpdateAsync(USER_ID, customerId: 11,
            new CustomerUpdateInfo { Name = "Maria S. Souza" });

        // Assert
        _repository.Verify(r => r.UpdateAsync(It.Is<Customer>(c =>
            c.Name == "Maria S. Souza" && c.UpdatedAt == NOW)), Times.Once);
        Assert.Equal("Maria S. Souza", result.Name);
    }

    [Fact]
    public async Task UpdateAsync_ShouldNotRehashOrReencryptTheCpf()
    {
        // Arrange: atualizar o nome não recriptografa nem rehasha o documento.
        _repository.Setup(r => r.GetByIdAsync(11, USER_ID)).ReturnsAsync(NewCustomer());

        // Act
        await _sut.UpdateAsync(USER_ID, 11, new CustomerUpdateInfo { Name = "Maria Teresa" });

        // Assert
        _cpfProtection.Verify(p => p.Hash(It.IsAny<string>()), Times.Never);
        _cpfProtection.Verify(p => p.Encrypt(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_ShouldThrowNotFound_WhenCustomerBelongsToAnotherUser()
    {
        // Arrange: o repositório filtra por userId (FR-022); o serviço vê null e responde 404.
        _repository.Setup(r => r.GetByIdAsync(11, USER_ID)).ReturnsAsync((Customer?)null);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<NotFoundException>(
            () => _sut.UpdateAsync(USER_ID, 11, new CustomerUpdateInfo { Name = "Vendedor invasor" }));

        Assert.Equal("Cliente não encontrado.", ex.Message);
        _repository.Verify(r => r.UpdateAsync(It.IsAny<Customer>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_ShouldThrowValidation_WhenNameIsInvalid()
    {
        await Assert.ThrowsAsync<ValidationException>(
            () => _sut.UpdateAsync(USER_ID, 11, new CustomerUpdateInfo { Name = "" }));

        _repository.Verify(r => r.UpdateAsync(It.IsAny<Customer>()), Times.Never);
    }
}
