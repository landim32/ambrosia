using System.Text.Json;
using Ambrosio.ApiTests.Fixtures;
using Ambrosio.DTO.Auth;
using Ambrosio.DTO.Enums;
using FluentAssertions;

namespace Ambrosio.ApiTests.Controllers;

/// <summary>
/// Cadastro, login com senha e Google ponta a ponta (quickstart, Cenário 1). Cada teste cria
/// seu próprio usuário por <c>POST /auth/register</c> com e-mail GUID — nenhum teste depende de
/// atalho sem senha nem de usuário semeado à mão (FR-016, SC-005).
/// </summary>
[Collection(ApiTestCollection.API_TESTS)]
public class AuthControllerTests
{
    private const string REGISTER = "/auth/register";
    private const string LOGIN = "/auth/login";
    private const string GOOGLE_LOGIN = "/auth/google";
    private const string VALID_PASSWORD = "senha-forte-1";

    /// <summary>Mensagem única do 401 (FR-008) — idêntica para e-mail inexistente e senha errada.</summary>
    private const string INVALID_CREDENTIALS = "E-mail ou senha inválidos";

    private readonly ApiTestFixture _fixture;

    public AuthControllerTests(ApiTestFixture fixture) => _fixture = fixture;

    // ---------------------------------------------------------------------------
    // US1 · POST /auth/register (FR-001, FR-003, FR-005, FR-006)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Register_NewEmail_ShouldReturn201WithToken()
    {
        // Arrange: e-mail inédito — cada execução usa GUID (FR-016).
        var email = $"novo+{Guid.NewGuid():N}@exemplo.com";

        // Act
        var response = await _fixture.PostAsync(REGISTER, string.Empty,
            new RegisterInfo { Name = "Ana Cadastro", Email = email, Password = VALID_PASSWORD });

        // Assert: 201 com a sessão já iniciada (research §3 — sem Location).
        response.StatusCode.Should().Be(201);
        response.Location.Should().BeNull();

        var result = response.As<LoginResult>();
        result.Token.Should().NotBeNullOrWhiteSpace();
        result.ExpiresAt.Should().BeAfter(DateTime.UtcNow.AddMinutes(1));
        result.User.Provider.Should().Be(AuthProvider.Password);
        result.User.Email.Should().Be(email);
        result.User.Name.Should().Be("Ana Cadastro");

        // SC-007: nenhum campo de senha — nem o nome da chave — sai da API.
        AssertNoPasswordKey(response.Body);

        // O token é o mesmo esquema do login Google: funciona em rota protegida, no mesmo id.
        var me = await _fixture.GetAsync("/users/me", result.Token);
        me.StatusCode.Should().Be(200);
        me.As<Ambrosio.DTO.Users.UserInfo>().Id.Should().Be(result.User.Id);
    }

    [Fact]
    public async Task Register_DuplicateEmail_ShouldReturn409()
    {
        // Arrange
        var email = $"dup+{Guid.NewGuid():N}@exemplo.com";
        var first = await _fixture.PostAsync(REGISTER, string.Empty,
            new RegisterInfo { Name = "Primeira", Email = email, Password = VALID_PASSWORD });

        first.StatusCode.Should().Be(201);

        // Act
        var second = await _fixture.PostAsync(REGISTER, string.Empty,
            new RegisterInfo { Name = "Outra Pessoa", Email = email, Password = "outra-senha-1" });

        // Assert: 409 genérico, sem revelar quem criou a conta (FR-006).
        second.StatusCode.Should().Be(409);
        second.As<Ambrosio.DTO.Common.ErrorResult>().Mensagem
            .Should().Be("Este e-mail já está cadastrado.");

        // Que a senha da conta existente não foi trocada pelo cadastro recusado é o que
        // Login_OriginalPassword_StillWorks_AfterRejectedDuplicate_ShouldLogin prova.
    }

    [Fact]
    public async Task Register_SameEmailDifferentCaseAndSpaces_ShouldReturn409()
    {
        // Arrange: cadastrado em minúsculas...
        var email = $"case+{Guid.NewGuid():N}@exemplo.com";
        await _fixture.PostAsync(REGISTER, string.Empty,
            new RegisterInfo { Name = "Normalizada", Email = email, Password = VALID_PASSWORD });

        // Act: ...e tentado com caixa diferente e espaços nas pontas (FR-003 normalização).
        var response = await _fixture.PostAsync(REGISTER, string.Empty,
            new RegisterInfo
            {
                Name = "Outra",
                Email = $" {email.ToUpperInvariant()} ",
                Password = "outra-senha-1"
            });

        // Assert
        response.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Register_ShortPassword_ShouldReturn400WithCampoPassword()
    {
        // Act: 7 caracteres (o mínimo é 8).
        var response = await _fixture.PostAsync(REGISTER, string.Empty,
            new RegisterInfo
            {
                Name = "Curta",
                Email = $"curta+{Guid.NewGuid():N}@exemplo.com",
                Password = "1234567"
            });

        // Assert
        response.StatusCode.Should().Be(400);
        AssertFieldError(response.Body, "password", "A senha deve ter entre 8 e 128 caracteres.");
    }

    [Fact]
    public async Task Register_PasswordOver128_ShouldReturn400WithCampoPassword()
    {
        // Act: 129 caracteres.
        var response = await _fixture.PostAsync(REGISTER, string.Empty,
            new RegisterInfo
            {
                Name = "Longa",
                Email = $"longa+{Guid.NewGuid():N}@exemplo.com",
                Password = new string('a', 129)
            });

        // Assert
        response.StatusCode.Should().Be(400);
        AssertFieldError(response.Body, "password");
    }

    [Fact]
    public async Task Register_EmptyName_ShouldReturn400WithCampoName()
    {
        // Act: só espaços — o validator corta após Trim() (FR-003).
        var response = await _fixture.PostAsync(REGISTER, string.Empty,
            new RegisterInfo
            {
                Name = "   ",
                Email = $"nomevazio+{Guid.NewGuid():N}@exemplo.com",
                Password = VALID_PASSWORD
            });

        // Assert
        response.StatusCode.Should().Be(400);
        AssertFieldError(response.Body, "name", "O nome é obrigatório.");
    }

    [Fact]
    public async Task Register_NameOver120_ShouldReturn400WithCampoName()
    {
        // Act
        var response = await _fixture.PostAsync(REGISTER, string.Empty,
            new RegisterInfo
            {
                Name = new string('n', 121),
                Email = $"nomelongo+{Guid.NewGuid():N}@exemplo.com",
                Password = VALID_PASSWORD
            });

        // Assert
        response.StatusCode.Should().Be(400);
        AssertFieldError(response.Body, "name", "O nome deve ter no máximo 120 caracteres.");
    }

    [Fact]
    public async Task Register_InvalidEmail_ShouldReturn400WithCampoEmail()
    {
        // Act
        var response = await _fixture.PostAsync(REGISTER, string.Empty,
            new RegisterInfo { Name = "Ana", Email = "nao-e-um-email", Password = VALID_PASSWORD });

        // Assert
        response.StatusCode.Should().Be(400);
        AssertFieldError(response.Body, "email", "E-mail inválido.");
    }

    [Fact]
    public async Task Register_MalformedBody_ShouldReturn400()
    {
        // Act: JSON que não é o objeto esperado (FR-003 edge case "corpo malformado").
        var response = await _fixture.PostAsync(REGISTER, string.Empty, "isso não é um objeto");

        // Assert
        response.StatusCode.Should().Be(400);
        response.Body.Should().Contain("mensagem");
    }

    // ---------------------------------------------------------------------------
    // US2 · POST /auth/login (FR-002, FR-007, FR-008)
    // ---------------------------------------------------------------------------

    /// <summary>Cadastra um usuário de teste e devolve o par e-mail → senha.</summary>
    private async Task<(string Email, string Password)> RegisterUserAsync(string? password = null)
    {
        var email = $"login+{Guid.NewGuid():N}@exemplo.com";
        var passwordToSend = password ?? VALID_PASSWORD;

        var created = await _fixture.PostAsync(REGISTER, string.Empty,
            new RegisterInfo { Name = "Usuário de Login", Email = email, Password = passwordToSend });

        created.StatusCode.Should().Be(201, $"cadastro de pré-condição falhou: {created}");

        return (email, passwordToSend);
    }

    private Task<HttpCallResult> LoginAsync(string email, string password) =>
        _fixture.PostAsync(LOGIN, string.Empty, new LoginInfo { Email = email, Password = password });

    [Fact]
    public async Task Login_ValidCredentials_ShouldReturn200WithSameUserId()
    {
        // Arrange
        var (email, password) = await RegisterUserAsync();

        // Act
        var response = await LoginAsync(email, password);

        // Assert
        response.StatusCode.Should().Be(200);

        var result = response.As<LoginResult>();
        result.Token.Should().NotBeNullOrWhiteSpace();
        result.ExpiresAt.Should().BeAfter(DateTime.UtcNow.AddMinutes(1));
        result.User.Provider.Should().Be(AuthProvider.Password);

        // SC-007: nenhuma chave de senha na resposta.
        AssertNoPasswordKey(response.Body);

        // FR-007: mesmo esquema do login Google — o token funciona em rota protegida.
        var me = await _fixture.GetAsync("/users/me", result.Token);
        me.StatusCode.Should().Be(200);
        me.As<Ambrosio.DTO.Users.UserInfo>().Id.Should().Be(result.User.Id);
    }

    [Fact]
    public async Task Login_SameUserAsRegister_ShouldReturnTheSameId()
    {
        // Arrange: o id do cadastro...
        var email = $"mesmo+{Guid.NewGuid():N}@exemplo.com";
        var created = await _fixture.PostAsync(REGISTER, string.Empty,
            new RegisterInfo { Name = "Mesmo Usuario", Email = email, Password = VALID_PASSWORD });

        created.StatusCode.Should().Be(201);
        var registeredId = created.As<LoginResult>().User.Id;

        // Act: ...e o id do login.
        var login = await LoginAsync(email, VALID_PASSWORD);

        // Assert: o login não duplica a conta (FR-002).
        login.StatusCode.Should().Be(200);
        login.As<LoginResult>().User.Id.Should().Be(registeredId);
    }

    [Fact]
    public async Task Login_EmailWithDifferentCaseAndSpaces_ShouldReturn200()
    {
        // Arrange
        var (email, password) = await RegisterUserAsync();

        // Act: caixa diferente e espaços nas pontas (normalização idêntica à do cadastro).
        var response = await LoginAsync($"  {email.ToUpperInvariant()}  ", password);

        // Assert
        response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task Login_WrongPassword_ShouldReturn401Generic()
    {
        // Arrange
        var (email, _) = await RegisterUserAsync();

        // Act
        var response = await LoginAsync(email, "senha-errada-123");

        // Assert: 401 no formato ErrorResult, mensagem única e erros vazio (FR-008).
        response.StatusCode.Should().Be(401);

        var error = response.As<Ambrosio.DTO.Common.ErrorResult>();
        error.Sucesso.Should().BeFalse();
        error.Mensagem.Should().Be(INVALID_CREDENTIALS);
        error.Erros.Should().BeEmpty();
    }

    [Fact]
    public async Task Login_UnknownEmail_ShouldReturn401WithSameMessage()
    {
        // Act: e-mail que nunca foi cadastrado.
        var response = await LoginAsync($"ninguem+{Guid.NewGuid():N}@exemplo.com", "senha-errada-123");

        // Assert: a MESMA mensagem do caso de senha errada — SC-006, nada revela a existência.
        response.StatusCode.Should().Be(401);

        var error = response.As<Ambrosio.DTO.Common.ErrorResult>();
        error.Mensagem.Should().Be(INVALID_CREDENTIALS);
        error.Erros.Should().BeEmpty();
    }

    [Fact]
    public async Task Login_OriginalPassword_StillWorks_AfterRejectedDuplicate_ShouldLogin()
    {
        // Arrange: um segundo cadastro com o mesmo e-mail é recusado (409)...
        var (email, password) = await RegisterUserAsync();
        var duplicate = await _fixture.PostAsync(REGISTER, string.Empty,
            new RegisterInfo { Name = "Intrusa", Email = email, Password = "outra-senha-123" });

        duplicate.StatusCode.Should().Be(409);

        // Act: ...e só a senha da conta existente continua entrando.
        var original = await LoginAsync(email, password);
        var attempted = await LoginAsync(email, "outra-senha-123");

        // Assert (FR-006: o cadastro recusado não toca a conta existente).
        original.StatusCode.Should().Be(200);
        attempted.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task Login_EmptyBody_ShouldReturn400()
    {
        // Act
        var response = await _fixture.PostAsync(LOGIN, string.Empty, new LoginInfo());

        // Assert
        response.StatusCode.Should().Be(400);
        response.As<Ambrosio.DTO.Common.ErrorResult>().Mensagem.Should().Be("Dados inválidos.");
    }

    [Fact]
    public async Task Login_InvalidEmail_ShouldReturn400WithCampoEmail()
    {
        // Act
        var response = await LoginAsync("nao-e-um-email", VALID_PASSWORD);

        // Assert
        response.StatusCode.Should().Be(400);
        AssertFieldError(response.Body, "email", "E-mail inválido.");
    }

    [Fact]
    public async Task Login_MissingPassword_ShouldReturn400WithCampoPassword()
    {
        // Act
        var response = await LoginAsync("ana@exemplo.com", string.Empty);

        // Assert
        response.StatusCode.Should().Be(400);
        AssertFieldError(response.Body, "password", "A senha é obrigatória.");
    }

    [Fact]
    public async Task Login_MalformedBody_ShouldReturn400()
    {
        // Act: JSON que não é o objeto esperado (o binding falha antes do validator).
        var response = await _fixture.PostAsync(LOGIN, string.Empty, "isso não é um objeto");

        // Assert
        response.StatusCode.Should().Be(400);
        response.Body.Should().Contain("mensagem");
    }

    // ---------------------------------------------------------------------------
    // US4 · o fixture entra pela sequência cadastro → login
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task CreateUserAsync_ShouldRegisterAndLoginInSequence()
    {
        // Assert (US4 cenário 1/2): o token que a suíte usa veio do LOGIN, não de um atalho —
        // o próprio fixture já teria falhado se a senha cadastrada não funcionasse.
        var created = await _fixture.CreateUserAsync(
            $"sequencia+{Guid.NewGuid():N}@exemplo.com",
            "Usuário em Sequência");

        created.Token.Should().NotBeNullOrWhiteSpace();
        created.User.Provider.Should().Be(AuthProvider.Password);

        var me = await _fixture.GetAsync("/users/me", created.Token);

        me.StatusCode.Should().Be(200);
        me.As<Ambrosio.DTO.Users.UserInfo>().Id.Should().Be(created.User.Id);
        me.As<Ambrosio.DTO.Users.UserInfo>().Email.Should().Be(created.User.Email);
    }

    [Fact]
    public async Task FixtureUsers_AAndB_ShouldBeDistinctPasswordAccounts()
    {
        // Os dois usuários do fixture (isolamento US4) são contas por senha reais.
        var a = await _fixture.GetAsync("/users/me", _fixture.TokenA);
        var b = await _fixture.GetAsync("/users/me", _fixture.TokenB);

        a.As<Ambrosio.DTO.Users.UserInfo>().Provider.Should().Be(AuthProvider.Password);
        b.As<Ambrosio.DTO.Users.UserInfo>().Provider.Should().Be(AuthProvider.Password);
        a.As<Ambrosio.DTO.Users.UserInfo>().Id.Should().NotBe(b.As<Ambrosio.DTO.Users.UserInfo>().Id);
    }

    // ---------------------------------------------------------------------------
    // US3 · POST /auth/dev-login deixa de existir em TODOS os ambientes (FR-012)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task DevLogin_ShouldReturn404InAnyEnvironment()
    {
        // Act: mesmo corpo que a rota aceite (e-mail + nome), sem token.
        var response = await _fixture.PostAsync("/auth/dev-login", string.Empty,
            new { email = $"dev+{Guid.NewGuid():N}@exemplo.com", name = "Dev" });

        // Assert: 404 com corpo vazio — indistinguível de qualquer rota inexistente, sem
        // código condicional por ambiente (FR-012).
        var unknown = await _fixture.PostAsync("/auth/rota-inexistente", string.Empty,
            new { email = "x@x.com", name = "X" });

        response.StatusCode.Should().Be(404);
        response.Body.Should().BeEmpty();
        response.StatusCode.Should().Be(unknown.StatusCode);
        response.Body.Should().Be(unknown.Body);
    }

    [Fact]
    public async Task Swagger_ShouldListRegisterAndLogin_ButNotDevLogin()
    {
        // Act: o documento OpenAPI real, no ambiente em que a API está rodando.
        var response = await _fixture.GetAsync("/swagger/v1/swagger.json", token: string.Empty);

        // Assert
        response.StatusCode.Should().Be(200);
        response.Body.Should().Contain("/auth/register");
        response.Body.Should().Contain("/auth/login");

        // FR-012/SC-003: nada de dev-login — nem o caminho, nem o DTO (o filtro de documento
        // que escondia a rota em Production foi apagado junto com a rota).
        response.Body.Should().NotContain("dev-login");
        response.Body.Should().NotContain("DevLoginInfo");
    }

    // ---------------------------------------------------------------------------
    // Google (mantido — SC-009)
    // ---------------------------------------------------------------------------

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

    /// <summary>
    /// SC-007: nenhuma <b>chave</b> contendo "password" aparece no JSON. Delega para o helper
    /// compartilhado <see cref="Ambrosio.ApiTests.Helpers.AuthBodyAssertions"/> — uma busca por
    /// substring no corpo não serve mais, porque o valor de <c>user.provider</c> é "password".
    /// </summary>
    private static void AssertNoPasswordKey(string body) =>
        Ambrosio.ApiTests.Helpers.AuthBodyAssertions.ShouldHaveNoPasswordKey(body);

    /// <summary>Le o <c>ErrorResult</c> e confere o par campo → mensagem (FR-024).</summary>
    private static void AssertFieldError(string body, string field, string? message = null)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        root.GetProperty("sucesso").GetBoolean().Should().BeFalse();
        root.GetProperty("mensagem").GetString().Should().Be("Dados inválidos.");

        var errors = root.GetProperty("erros").EnumerateArray().ToList();

        errors.Should().ContainSingle(e => e.GetProperty("campo").GetString() == field);

        if (message is not null)
        {
            errors.Should().Contain(
                e => e.GetProperty("campo").GetString() == field
                    && e.GetProperty("mensagem").GetString() == message);
        }
    }
}
