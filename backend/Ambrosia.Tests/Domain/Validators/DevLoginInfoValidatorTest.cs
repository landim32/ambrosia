using Ambrosia.Domain.Validators;
using Ambrosia.DTO.Auth;

namespace Ambrosia.Tests.Domain.Validators;

/// <summary>
/// T029 [US1]: validação de formato dos DTOs de login, com mensagens em português citando
/// o campo (FR-024). Inclui os casos de <see cref="GoogleLoginInfoValidator"/>.
/// </summary>
public class DevLoginInfoValidatorTest
{
    private readonly DevLoginInfoValidator _sut = new();

    private static DevLoginInfo Valid(
        string email = "ana@exemplo.com",
        string name = "Ana") => new() { Email = email, Name = name };

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
    public async Task Validate_ShouldRequireEmail_WhenEmailIsMissing(string? email)
    {
        // Arrange
        var info = Valid(email: email!);

        // Act
        var result = await _sut.ValidateAsync(info);

        // Assert
        var error = Assert.Single(result.Errors, e => e.PropertyName == "Email");
        Assert.Equal("O e-mail é obrigatório.", error.ErrorMessage);
    }

    [Theory]
    [InlineData("não-é-email")]
    [InlineData("ana@")]
    [InlineData("@exemplo.com")]
    public async Task Validate_ShouldRejectBadEmailFormat_WhenEmailIsInvalid(string email)
    {
        // Act
        var result = await _sut.ValidateAsync(Valid(email));

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Email" && e.ErrorMessage == "E-mail inválido.");
    }

    [Fact]
    public async Task Validate_ShouldRejectEmailLongerThanColumn_WhenEmailExceeds260Chars()
    {
        // Arrange: users.email é varchar(260) (data-model).
        var email = new string('a', 253) + "@exemplo.com"; // 266 caracteres

        // Act
        var result = await _sut.ValidateAsync(Valid(email));

        // Assert
        Assert.Contains(result.Errors, e =>
            e.PropertyName == "Email" && e.ErrorMessage.Contains("260"));
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
    public async Task Validate_ShouldRejectNameOver200_WhenNameIsTooLong()
    {
        // Act
        var result = await _sut.ValidateAsync(Valid(name: new string('n', 201)));

        // Assert
        Assert.Equal("O nome deve ter no máximo 200 caracteres.",
            Assert.Single(result.Errors, e => e.PropertyName == "Name").ErrorMessage);
    }

    [Fact]
    public async Task Validate_ShouldAcceptNameOfExactly200_WhenNameHitsTheLimit()
    {
        // Act
        var result = await _sut.ValidateAsync(Valid(name: new string('n', 200)));

        // Assert
        Assert.True(result.IsValid);
    }
}

/// <summary>
/// T029 [US1]: <c>idToken</c> obrigatório no fluxo Google (casos incluídos no mesmo arquivo,
/// conforme a tarefa).
/// </summary>
public class GoogleLoginInfoValidatorTest
{
    private readonly GoogleLoginInfoValidator _sut = new();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Validate_ShouldRequireIdToken_WhenTokenIsMissing(string? idToken)
    {
        // Act
        var result = await _sut.ValidateAsync(new GoogleLoginInfo { IdToken = idToken! });

        // Assert
        var error = Assert.Single(result.Errors, e => e.PropertyName == "IdToken");
        Assert.Equal("O token do Google é obrigatório.", error.ErrorMessage);
    }

    [Fact]
    public async Task Validate_ShouldSucceed_WithANonEmptyIdToken()
    {
        // Act
        var result = await _sut.ValidateAsync(new GoogleLoginInfo { IdToken = "eyJhbGciOi..." });

        // Assert: a validação criptográfica do token é do AppService (T044), não do validator.
        Assert.True(result.IsValid);
    }
}
