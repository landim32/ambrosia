using System.Text.Json;
using Ambrosia.ApiTests.Fixtures;
using Ambrosia.DTO.Auth;
using Ambrosia.DTO.Enums;
using FluentAssertions;

namespace Ambrosia.ApiTests.Controllers;

/// <summary>
/// T032 [US1]: <c>POST /auth/dev-login</c> e <c>POST /auth/google</c> ponta a ponta
/// (quickstart, Cenário 1).
/// </summary>
[Collection(ApiTestCollection.API_TESTS)]
public class AuthControllerTests
{
    private const string DEV_LOGIN = "/auth/dev-login";
    private const string GOOGLE_LOGIN = "/auth/google";

    private readonly ApiTestFixture _fixture;

    public AuthControllerTests(ApiTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task DevLogin_NewEmail_ShouldReturn200WithToken()
    {
        // Arrange: e-mail inédito nesta sessão.
        var email = $"novo+{Guid.NewGuid():N}@exemplo.com";

        // Act: a rota é [AllowAnonymous] — nenhuma Authorization é enviada.
        var response = await _fixture.PostAsync(DEV_LOGIN, string.Empty,
            new DevLoginInfo { Email = email, Name = "Novo Usuário" });

        // Assert
        response.StatusCode.Should().Be(200);

        var result = response.As<LoginResult>();
        result.Token.Should().NotBeNullOrWhiteSpace();
        result.ExpiresAt.Should().BeAfter(DateTime.UtcNow.AddMinutes(1));
        result.User.Id.Should().BeGreaterThan(0);
        result.User.Email.Should().Be(email);
        result.User.Name.Should().Be("Novo Usuário");
        result.User.Provider.Should().Be(AuthProvider.Dev);
    }

    [Fact]
    public async Task DevLogin_SameEmailTwice_ShouldReturnSameUserId()
    {
        // Arrange (Cenário 1 do quickstart: repetir o dev-login não duplica o usuário).
        var email = $"repetido+{Guid.NewGuid():N}@exemplo.com";

        // Act: o segundo uso muda a caixa do e-mail para provar a normalização
        // (Trim().ToLowerInvariant) no upsert.
        var first = await _fixture.PostAsync(DEV_LOGIN, string.Empty,
            new DevLoginInfo { Email = email, Name = "Repetido" });
        var second = await _fixture.PostAsync(DEV_LOGIN, string.Empty,
            new DevLoginInfo { Email = email.ToUpperInvariant(), Name = "Repetido" });

        // Assert
        first.StatusCode.Should().Be(200);
        second.StatusCode.Should().Be(200);
        second.As<LoginResult>().User.Id.Should().Be(first.As<LoginResult>().User.Id);
    }

    [Fact]
    public async Task DevLogin_InvalidEmail_ShouldReturn400WithCampoEmail()
    {
        // Act (FR-024: mensagem em português citando o campo)
        var response = await _fixture.PostAsync(DEV_LOGIN, string.Empty,
            new DevLoginInfo { Email = "nao-e-um-email", Name = "Ana" });

        // Assert
        response.StatusCode.Should().Be(400);

        using var document = JsonDocument.Parse(response.Body);
        var root = document.RootElement;

        root.GetProperty("sucesso").GetBoolean().Should().BeFalse();
        root.GetProperty("mensagem").GetString().Should().Be("Dados inválidos.");
        root.GetProperty("erros").EnumerateArray()
            .Should().Contain(e => e.GetProperty("campo").GetString() == "email");
    }

    [Fact]
    public async Task Google_InvalidIdToken_ShouldReturn400()
    {
        // Act: um ID token que não é um JWT emitido pelo Google (research §1).
        var response = await _fixture.PostAsync(GOOGLE_LOGIN, string.Empty,
            new GoogleLoginInfo { IdToken = "token-invalido-que-nao-e-jwt" });

        // Assert
        response.StatusCode.Should().Be(400);
        response.Body.Should().Contain("idToken");
    }

    [Fact]
    public async Task Google_MissingIdToken_ShouldReturn400WithCampoIdToken()
    {
        // Act: corpo vazio → regra "O token do Google é obrigatório." (T039).
        var response = await _fixture.PostAsync(GOOGLE_LOGIN, string.Empty, new { });

        // Assert
        response.StatusCode.Should().Be(400);
        response.Body.Should().Contain("idToken");
    }
}
