using Ambrosia.Domain.Validators;
using Ambrosia.DTO.Customers;

namespace Ambrosia.Tests.Domain.Validators;

/// <summary>
/// T052 [US2]: validação de formato de cliente (FR-006, FR-007) com mensagens em português
/// citando o campo (FR-024). Inclui os casos de <see cref="CustomerUpdateInfoValidator"/> e
/// <see cref="CustomerListQueryValidator"/>, conforme a tarefa.
/// </summary>
public class CustomerInsertInfoValidatorTest
{
    private readonly CustomerInsertInfoValidator _sut = new();

    private static CustomerInsertInfo Valid(string name = "Maria Silva", string cpf = "529.982.247-25")
        => new() { Name = name, Cpf = cpf };

    [Fact]
    public async Task Validate_ShouldSucceed_WithWellFormedPayload()
    {
        // Act
        var result = await _sut.ValidateAsync(Valid());

        // Assert
        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => e.ErrorMessage)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Validate_ShouldRequireName_WhenNameIsMissing(string? name)
    {
        // Act
        var result = await _sut.ValidateAsync(Valid(name: name!));

        // Assert
        var error = Assert.Single(result.Errors, e => e.PropertyName == "Name");
        Assert.Equal("O nome é obrigatório.", error.ErrorMessage);
    }

    [Fact]
    public async Task Validate_ShouldRejectOneCharacterName_WhenNameIsTooShort()
    {
        // data-model: name exige 2-200 caracteres.
        var result = await _sut.ValidateAsync(Valid(name: "M"));

        Assert.Equal("O nome deve ter entre 2 e 200 caracteres.",
            Assert.Single(result.Errors, e => e.PropertyName == "Name").ErrorMessage);
    }

    [Fact]
    public async Task Validate_ShouldAcceptNameOfExactly200_WhenNameHitsTheLimit()
    {
        var result = await _sut.ValidateAsync(Valid(name: new string('m', 200)));

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task Validate_ShouldRejectNameOver200_WhenNameIsTooLong()
    {
        var result = await _sut.ValidateAsync(Valid(name: new string('m', 201)));

        Assert.Equal("O nome deve ter entre 2 e 200 caracteres.",
            Assert.Single(result.Errors, e => e.PropertyName == "Name").ErrorMessage);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Validate_ShouldRequireCpf_WhenCpfIsMissing(string? cpf)
    {
        // Act
        var result = await _sut.ValidateAsync(Valid(cpf: cpf!));

        // Assert
        var error = Assert.Single(result.Errors, e => e.PropertyName == "Cpf");
        Assert.Equal("O CPF é obrigatório.", error.ErrorMessage);
    }

    [Theory]
    [InlineData("111.111.111-11")]
    [InlineData("529.982.247-26")]
    [InlineData("5299822472")]
    [InlineData("abc")]
    public async Task Validate_ShouldRejectInvalidCpf_WhenCheckDigitsOrLengthFail(string cpf)
    {
        // Act: FR-007 — o validator delega a regra a Cpf.IsValid (research §5).
        var result = await _sut.ValidateAsync(Valid(cpf: cpf));

        // Assert
        Assert.Contains(result.Errors, e => e.PropertyName == "Cpf" && e.ErrorMessage == "CPF inválido.");
    }

    [Theory]
    [InlineData("52998224725")]
    [InlineData("529.982.247-25")]
    public async Task Validate_ShouldAcceptAnyFormatting_WhenCpfIsValid(string cpf)
    {
        // Act: a spec aceita com ou sem pontuação (FR-006).
        var result = await _sut.ValidateAsync(Valid(cpf: cpf));

        // Assert
        Assert.True(result.IsValid);
    }
}

/// <summary>
/// T052 [US2]: <c>PUT /customers/{id}</c> só muda o nome, com as mesmas regras de formato
/// (FR-009).
/// </summary>
public class CustomerUpdateInfoValidatorTest
{
    private readonly CustomerUpdateInfoValidator _sut = new();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Validate_ShouldRequireName_WhenNameIsMissing(string? name)
    {
        var result = await _sut.ValidateAsync(new CustomerUpdateInfo { Name = name! });

        var error = Assert.Single(result.Errors, e => e.PropertyName == "Name");
        Assert.Equal("O nome é obrigatório.", error.ErrorMessage);
    }

    [Fact]
    public async Task Validate_ShouldRejectOneCharacter_WhenNameIsTooShort()
    {
        var result = await _sut.ValidateAsync(new CustomerUpdateInfo { Name = "a" });

        Assert.Equal("O nome deve ter entre 2 e 200 caracteres.",
            Assert.Single(result.Errors, e => e.PropertyName == "Name").ErrorMessage);
    }

    [Fact]
    public async Task Validate_ShouldRejectOver200_WhenNameIsTooLong()
    {
        var result = await _sut.ValidateAsync(new CustomerUpdateInfo { Name = new string('b', 201) });

        Assert.Equal("O nome deve ter entre 2 e 200 caracteres.",
            Assert.Single(result.Errors, e => e.PropertyName == "Name").ErrorMessage);
    }

    [Fact]
    public async Task Validate_ShouldSucceed_WithNameInRange()
    {
        var result = await _sut.ValidateAsync(new CustomerUpdateInfo { Name = "Maria S. Souza" });

        Assert.True(result.IsValid);
    }
}

/// <summary>
/// T052 [US2]: limites de paginação da listagem (FR-008, research §9) — página ≥ 1 e
/// pageSize entre 1 e 100.
/// </summary>
public class CustomerListQueryValidatorTest
{
    private readonly CustomerListQueryValidator _sut = new();

    [Fact]
    public async Task Validate_ShouldSucceed_WithDefaults()
    {
        // Act: sem parâmetros na query, page=1 e pageSize=20 (padrões do DTO).
        var result = await _sut.ValidateAsync(new CustomerListQuery());

        // Assert
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Validate_ShouldRejectPageBelowOne_WhenPageIsInvalid(int page)
    {
        var result = await _sut.ValidateAsync(new CustomerListQuery { Page = page });

        var error = Assert.Single(result.Errors, e => e.PropertyName == "Page");
        Assert.Equal("A página deve ser maior ou igual a 1.", error.ErrorMessage);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task Validate_ShouldRejectPageSizeOutOfRange_WhenPageSizeIsInvalid(int pageSize)
    {
        var result = await _sut.ValidateAsync(new CustomerListQuery { PageSize = pageSize });

        var error = Assert.Single(result.Errors, e => e.PropertyName == "PageSize");
        Assert.Equal("O tamanho da página deve estar entre 1 e 100.", error.ErrorMessage);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public async Task Validate_ShouldAcceptBoundaryPageSize_WhenWithinLimits(int pageSize)
    {
        var result = await _sut.ValidateAsync(new CustomerListQuery { PageSize = pageSize });

        Assert.True(result.IsValid);
    }
}
