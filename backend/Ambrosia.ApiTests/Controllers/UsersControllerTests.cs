using Ambrosia.ApiTests.Fixtures;
using Ambrosia.ApiTests.Helpers;
using Ambrosia.DTO.Enums;
using Ambrosia.DTO.Users;
using FluentAssertions;
using Flurl;
using Flurl.Http;

namespace Ambrosia.ApiTests.Controllers;

/// <summary>
/// T033 [US1]: <c>GET /users/me</c> devolve o próprio perfil sem senha (FR-003) e exige
/// token válido (SC-004).
/// </summary>
[Collection(ApiTestCollection.API_TESTS)]
public class UsersControllerTests
{
    private const string ME = "/users/me";

    private readonly ApiTestFixture _fixture;

    public UsersControllerTests(ApiTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Me_WithToken_ShouldReturnProfileWithoutPassword()
    {
        // Act
        var response = await _fixture.GetAsync(ME, _fixture.TokenA);

        // Assert: nenhuma chave contendo "password" aparece no JSON bruto (FR-003).
        response.StatusCode.Should().Be(200);
        response.Body.Should().NotContain("password", "o hash de senha nunca pode ser mapeado para um DTO");

        var user = response.As<UserInfo>();
        user.Id.Should().Be(_fixture.UserIdA);
        user.Email.Should().Be(_fixture.EmailA);
        user.Name.Should().Be("Ana");
        user.Provider.Should().Be(AuthProvider.Dev);
        user.CreatedAt.Should().NotBe(default);
    }

    [Fact]
    public async Task Me_WithoutToken_ShouldReturn401()
    {
        // Act: sem cabeçalho Authorization (corpo vazio pelo middleware JwtBearer, research §8).
        var response = await _fixture.GetAsync(ME, token: string.Empty);

        // Assert
        response.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task Me_WithTamperedToken_ShouldReturn401()
    {
        // Arrange: assinatura adulterada em um caractere; o token continua bem formado (3 partes).
        var tampered = JwtHelpers.TamperSignature(_fixture.TokenA);

        // Act
        var response = await new Url(_fixture.BaseUrl)
            .AppendPathSegment("users/me")
            .WithOAuthBearerToken(tampered)
            .AllowAnyHttpStatus()
            .GetAsync();

        // Assert
        response.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task Me_ShouldReturnDifferentProfiles_ForDifferentUsers()
    {
        // Act: A e B do fixture (base dos testes de isolamento da US4).
        var a = (await _fixture.GetAsync(ME, _fixture.TokenA)).As<UserInfo>();
        var b = (await _fixture.GetAsync(ME, _fixture.TokenB)).As<UserInfo>();

        // Assert
        a.Id.Should().NotBe(b.Id);
        a.Email.Should().Be(_fixture.EmailA);
        b.Email.Should().Be(_fixture.EmailB);
    }
}
