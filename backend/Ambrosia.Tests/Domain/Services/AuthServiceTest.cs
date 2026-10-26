using Ambrosia.Domain.Exceptions;
using Ambrosia.Domain.Interfaces;
using Ambrosia.Domain.Models;
using Ambrosia.Domain.Services;
using Ambrosia.DTO.Auth;
using Ambrosia.DTO.Enums;
using Ambrosia.Infra.Interfaces.AppServices;
using Ambrosia.Infra.Interfaces.Repository;
using Ambrosia.Tests.TestHelpers;
using FluentValidation;
using FluentValidation.Results;
using Moq;

namespace Ambrosia.Tests.Domain.Services;

/// <summary>
/// T028 [US1]: upsert do usuário por e-mail normalizado nos dois fluxos de login
/// (Google OIDC e dev-login) e o mapeamento de falhas do provedor em ValidationException.
/// </summary>
public class AuthServiceTest
{
    private const string TOKEN = "jwt-do-ambrosia";
    private static readonly DateTime NOW = new(2026, 10, 25, 12, 0, 0, DateTimeKind.Unspecified);

    private readonly Mock<IUserRepository<User>> _users = new();
    private readonly Mock<IOAuthTokenAppService> _oauth = new();
    private readonly Mock<IJwtTokenAppService> _jwt = new();
    private readonly Mock<IValidator<GoogleLoginInfo>> _googleValidator = new();
    private readonly Mock<IValidator<DevLoginInfo>> _devValidator = new();
    private readonly FakeClock _clock = new(NOW);
    private readonly AuthService _sut;

    public AuthServiceTest()
    {
        _jwt.Setup(s => s.Generate(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns((TOKEN, NOW.AddMinutes(480)));

        // Por padrão os validators passam: os testes de regra de formato estão em
        // DevLoginInfoValidatorTest (T029).
        _googleValidator.Setup(v => v.ValidateAsync(It.IsAny<GoogleLoginInfo>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());
        _devValidator.Setup(v => v.ValidateAsync(It.IsAny<DevLoginInfo>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());

        _sut = new AuthService(
            _users.Object, _oauth.Object, _jwt.Object,
            _googleValidator.Object, _devValidator.Object, _clock);
    }

    private static OAuthUserInfo Provider(string email, bool verified = true, string name = "Ana", string subject = "sub-123")
        => new()
        {
            Email = email,
            EmailVerified = verified,
            Name = name,
            Subject = subject
        };

    [Fact]
    public async Task LoginWithGoogleAsync_ShouldCreateUserWithNormalizedEmail_WhenEmailIsNew()
    {
        // Arrange
        _oauth.Setup(o => o.ValidateIdTokenAsync("id-token")).ReturnsAsync(Provider("  ANA@Exemplo.COM "));
        _users.Setup(r => r.GetByEmailAsync("ana@exemplo.com")).ReturnsAsync((User?)null);
        _users.Setup(r => r.InsertAsync(It.IsAny<User>()))
            .ReturnsAsync((User u) => u);

        // Act
        var result = await _sut.LoginWithGoogleAsync(new GoogleLoginInfo { IdToken = "id-token" });

        // Assert
        _users.Verify(r => r.InsertAsync(It.Is<User>(u =>
            u.Email == "ana@exemplo.com"
            && u.Provider == AuthProvider.Google
            && u.ProviderSubject == "sub-123"
            && u.Name == "Ana"
            && u.CreatedAt == NOW
            && u.UpdatedAt == NOW)), Times.Once);

        Assert.Equal(TOKEN, result.Token);
        Assert.Equal("ana@exemplo.com", result.User.Email);
    }

    [Fact]
    public async Task LoginWithGoogleAsync_ShouldUseEmailLocalPart_WhenProviderNameIsEmpty()
    {
        // Arrange
        _oauth.Setup(o => o.ValidateIdTokenAsync(It.IsAny<string>()))
            .ReturnsAsync(Provider("ana@exemplo.com", name: "   "));
        _users.Setup(r => r.GetByEmailAsync(It.IsAny<string>())).ReturnsAsync((User?)null);
        _users.Setup(r => r.InsertAsync(It.IsAny<User>())).ReturnsAsync((User u) => u);

        // Act
        var result = await _sut.LoginWithGoogleAsync(new GoogleLoginInfo { IdToken = "id-token" });

        // Assert
        Assert.Equal("ana", result.User.Name);
    }

    [Fact]
    public async Task LoginWithGoogleAsync_ShouldUpdateExistingUserAndNotInsert_WhenEmailAlreadyExists()
    {
        // Arrange: segundo login com o mesmo e-mail em caixa diferente (data-model: nome/subject
        // podem mudar no provedor e UpdatedAt é atualizado a cada login).
        var existing = new User
        {
            Id = 7,
            Email = "ana@exemplo.com",
            Name = "Nome Antigo",
            Provider = AuthProvider.Google,
            ProviderSubject = "sub-antigo",
            CreatedAt = NOW.AddDays(-30),
            UpdatedAt = NOW.AddDays(-30)
        };

        _oauth.Setup(o => o.ValidateIdTokenAsync(It.IsAny<string>()))
            .ReturnsAsync(Provider("ANA@exemplo.com", name: "Ana Souza", subject: "sub-novo"));
        _users.Setup(r => r.GetByEmailAsync("ana@exemplo.com")).ReturnsAsync(existing);
        _users.Setup(r => r.UpdateAsync(It.IsAny<User>())).ReturnsAsync((User u) => u);

        // Act
        var result = await _sut.LoginWithGoogleAsync(new GoogleLoginInfo { IdToken = "id-token" });

        // Assert
        _users.Verify(r => r.InsertAsync(It.IsAny<User>()), Times.Never);
        _users.Verify(r => r.UpdateAsync(It.Is<User>(u =>
            u.Id == 7
            && u.Name == "Ana Souza"
            && u.ProviderSubject == "sub-novo"
            && u.UpdatedAt == NOW)), Times.Once);

        Assert.Equal(7, result.User.Id);
    }

    [Fact]
    public async Task LoginWithGoogleAsync_ShouldThrowValidationOnIdToken_WhenProviderTokenIsInvalid()
    {
        // Arrange: ID token inválido/expirado → AppService devolve null (T044).
        _oauth.Setup(o => o.ValidateIdTokenAsync(It.IsAny<string>())).ReturnsAsync((OAuthUserInfo?)null);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => _sut.LoginWithGoogleAsync(new GoogleLoginInfo { IdToken = "ruim" }));

        Assert.Equal("idToken", ex.Errors.Single().PropertyName);
        Assert.Equal("Token do Google inválido ou expirado.", ex.Errors.Single().ErrorMessage);
        _users.Verify(r => r.InsertAsync(It.IsAny<User>()), Times.Never);
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("ana@exemplo.com", false)]
    public async Task LoginWithGoogleAsync_ShouldThrowValidation_WhenProviderHasNoVerifiedEmail(
        string email, bool verified)
    {
        // Arrange
        _oauth.Setup(o => o.ValidateIdTokenAsync(It.IsAny<string>()))
            .ReturnsAsync(Provider(email, verified));

        // Act & Assert (FR-001 exige e-mail verificado).
        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => _sut.LoginWithGoogleAsync(new GoogleLoginInfo { IdToken = "id-token" }));

        Assert.Equal("O provedor não retornou um e-mail verificado.", ex.Errors.Single().ErrorMessage);
        _users.Verify(r => r.InsertAsync(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task LoginWithGoogleAsync_ShouldRethrowExistingUser_WhenInsertLosesTheRace()
    {
        // Arrange: corrida entre duas sessões do mesmo e-mail (research §11) — o índice único
        // devolve DuplicateException e o serviço deve reler por e-mail.
        var created = new User { Id = 11, Email = "ana@exemplo.com", Name = "Ana", Provider = AuthProvider.Google };

        _oauth.Setup(o => o.ValidateIdTokenAsync(It.IsAny<string>())).ReturnsAsync(Provider("ana@exemplo.com"));
        _users.SetupSequence(r => r.GetByEmailAsync("ana@exemplo.com"))
            .ReturnsAsync((User?)null)   // primeira leitura: ainda não existe
            .ReturnsAsync(created);       // releitura após o empate
        _users.Setup(r => r.InsertAsync(It.IsAny<User>()))
            .ThrowsAsync(new DuplicateException("Já existe um usuário com este e-mail."));

        // Act
        var result = await _sut.LoginWithGoogleAsync(new GoogleLoginInfo { IdToken = "id-token" });

        // Assert
        Assert.Equal(11, result.User.Id);
    }

    [Fact]
    public async Task DevLoginAsync_ShouldCreateUserWithProviderDev_WhenEmailIsNew()
    {
        // Arrange
        _users.Setup(r => r.GetByEmailAsync("ana@exemplo.com")).ReturnsAsync((User?)null);
        _users.Setup(r => r.InsertAsync(It.IsAny<User>())).ReturnsAsync((User u) => u);

        // Act
        var result = await _sut.DevLoginAsync(new DevLoginInfo
        {
            Email = "ana@exemplo.com",
            Name = "Ana"
        });

        // Assert (T028: dev-login cria usuário com Provider = Dev)
        _users.Verify(r => r.InsertAsync(It.Is<User>(u =>
            u.Provider == AuthProvider.Dev
            && u.Email == "ana@exemplo.com"
            && u.Name == "Ana")), Times.Once);

        Assert.Equal(TOKEN, result.Token);
        Assert.Equal(AuthProvider.Dev, result.User.Provider);
    }

    [Fact]
    public async Task DevLoginAsync_ShouldReuseUserAndReturnSameId_WhenEmailAlreadyExists()
    {
        // Arrange: cenário 1 do quickstart — dev-login duas vezes com o mesmo e-mail não duplica.
        var existing = new User
        {
            Id = 5,
            Email = "ana@exemplo.com",
            Name = "Ana",
            Provider = AuthProvider.Dev
        };

        _users.Setup(r => r.GetByEmailAsync("ana@exemplo.com")).ReturnsAsync(existing);
        _users.Setup(r => r.UpdateAsync(It.IsAny<User>())).ReturnsAsync((User u) => u);

        // Act
        var result = await _sut.DevLoginAsync(new DevLoginInfo { Email = "ana@exemplo.com", Name = "Ana" });

        // Assert
        Assert.Equal(5, result.User.Id);
        _users.Verify(r => r.InsertAsync(It.IsAny<User>()), Times.Never);
    }
}
