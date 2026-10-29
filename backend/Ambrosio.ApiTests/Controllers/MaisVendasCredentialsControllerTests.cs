using System.Text.Json;
using Ambrosio.ApiTests.Fixtures;
using Ambrosio.ApiTests.Helpers;
using Ambrosio.DTO.Common;
using Ambrosio.DTO.Enums;
using Ambrosio.DTO.Imports;
using FluentAssertions;

namespace Ambrosio.ApiTests.Controllers;

/// <summary>
/// Credenciais do +Vendas na tela de configuração (feature 008, US1 — T012). Rotas
/// <c>GET/PUT/DELETE /imports/mais-vendas/credentials</c>.
/// <para>
/// Cada teste cria a <b>própria vendedora</b>: as credenciais são uma linha por usuário, e dividir a
/// linha entre testes faria um cadastro aparecer dentro de outro. A senha usada é
/// <see cref="TestDataHelper.TRACEABLE_PASSWORD"/> — string de teste, não senha de ninguém — porque a
/// asserção central da história é que ela não reaparece em resposta alguma (SC-003/FR-003).
/// </para>
/// </summary>
[Collection(ApiTestCollection.API_TESTS)]
public class MaisVendasCredentialsControllerTests
{
    private const string CREDENTIALS = "/imports/mais-vendas/credentials";
    private const string DEFAULTS = "/imports/mais-vendas/defaults";
    private const string RUNS = "/imports/mais-vendas/runs";

    /// <summary>Login de teste: formatado como o do +Vendas em homologação (conta+cenário).</summary>
    private const string LOGIN = "s482+ok";

    /// <summary>Mesmo teto da coluna e do validator.</summary>
    private const int LOGIN_MAX = 120;

    private const int PASSWORD_MAX = 200;

    private readonly ApiTestFixture _fixture;

    public MaisVendasCredentialsControllerTests(ApiTestFixture fixture) => _fixture = fixture;

    // =====================================================================================
    // GET
    // =====================================================================================

    [Fact]
    public async Task Get_NoCredentials_ReturnsNoneWithoutLogin()
    {
        var (token, _) = await _fixture.NewSellerAsync();

        var response = await _fixture.GetAsync(CREDENTIALS, token);

        response.StatusCode.Should().Be(200, response.ToString());

        var info = response.As<MaisVendasCredentialsInfo>();

        info.HasPassword.Should().BeFalse();
        info.Login.Should().BeEmpty();
        info.Status.Should().Be(MaisVendasCredentialsStatus.None);
        info.SavedAt.Should().BeNull();
        info.PasswordChangedAt.Should().BeNull();
    }

    [Fact]
    public async Task Get_AfterSave_ReturnsLoginAndDatesButNeverThePassword()
    {
        var (token, _) = await _fixture.NewSellerAsync();

        await _fixture.SaveCredentialsAsync(token, LOGIN, TestDataHelper.TRACEABLE_PASSWORD);

        var response = await _fixture.GetAsync(CREDENTIALS, token);

        response.StatusCode.Should().Be(200, response.ToString());

        var info = response.As<MaisVendasCredentialsInfo>();

        // O login volta inteiro para a vendedora editar o próprio cadastro (contrato); a senha não
        // volta de jeito nenhum — nem mascarada, nem como chave nula.
        info.Login.Should().Be(LOGIN);
        info.HasPassword.Should().BeTrue();
        HasPasswordProperty(response.Body).Should().BeFalse();
        info.Status.Should().Be(MaisVendasCredentialsStatus.Valid);
        info.PasswordChangedAt.Should().NotBeNull();

        response.Body.Should().NotContain(TestDataHelper.TRACEABLE_PASSWORD);
    }

    [Fact]
    public async Task Get_Anonymous_Returns401()
    {
        // Token vazio = requisição sem cabeçalho Authorization (ApiTestFixture).
        var response = await _fixture.GetAsync(CREDENTIALS, string.Empty);

        response.StatusCode.Should().Be(401, response.Body);
    }

    // =====================================================================================
    // PUT — cadastro e troca
    // =====================================================================================

    [Fact]
    public async Task Put_FirstSave_ReturnsValidAndKeepsPasswordOut()
    {
        var (token, _) = await _fixture.NewSellerAsync();

        var response = await _fixture.SaveCredentialsAsync(token, LOGIN, TestDataHelper.TRACEABLE_PASSWORD);

        response.StatusCode.Should().Be(200, response.ToString());

        var info = response.As<MaisVendasCredentialsInfo>();

        info.HasPassword.Should().BeTrue();
        info.Login.Should().Be(LOGIN);
        HasPasswordProperty(response.Body).Should().BeFalse();
        info.Status.Should().Be(MaisVendasCredentialsStatus.Valid);
        info.SavedAt.Should().NotBeNull();
        info.PasswordChangedAt.Should().NotBeNull();

        // SC-003: nem no corpo, nem no eco do login.
        response.Body.Should().NotContain(TestDataHelper.TRACEABLE_PASSWORD);
    }

    [Fact]
    public async Task Put_NewPasswordWithoutLogin_KeepsLoginAndReturnsValid()
    {
        var (token, _) = await _fixture.NewSellerAsync();

        await _fixture.SaveCredentialsAsync(token, LOGIN, "senha-antiga");

        var response = await _fixture.SaveCredentialsAsync(token, null, TestDataHelper.TRACEABLE_PASSWORD);

        response.StatusCode.Should().Be(200, response.ToString());

        var info = response.As<MaisVendasCredentialsInfo>();

        info.Login.Should().Be(LOGIN);
        info.Status.Should().Be(MaisVendasCredentialsStatus.Valid);

        var current = (await _fixture.GetAsync(CREDENTIALS, token)).As<MaisVendasCredentialsInfo>();

        current.Login.Should().Be(LOGIN);
        current.HasPassword.Should().BeTrue();
    }

    [Fact]
    public async Task Put_SameLoginWithoutPassword_ChangesNothing()
    {
        var (token, _) = await _fixture.NewSellerAsync();

        await _fixture.SaveCredentialsAsync(token, LOGIN, TestDataHelper.TRACEABLE_PASSWORD);

        var before = (await _fixture.GetAsync(CREDENTIALS, token)).As<MaisVendasCredentialsInfo>();

        var response = await _fixture.SaveCredentialsAsync(token, LOGIN, null);

        response.StatusCode.Should().Be(200, response.ToString());

        var after = (await _fixture.GetAsync(CREDENTIALS, token)).As<MaisVendasCredentialsInfo>();

        after.Login.Should().Be(LOGIN);
        after.HasPassword.Should().BeTrue();
        after.PasswordChangedAt.Should().Be(before.PasswordChangedAt);
    }

    [Theory]
    [InlineData("", "senha-qualquer", "login", "Informe o login do +Vendas.")]
    [InlineData("   ", "senha-qualquer", "login", "Informe o login do +Vendas.")]
    [InlineData(LOGIN, "", "password", "Informe a senha do +Vendas.")]
    [InlineData(LOGIN, "   ", "password", "Informe a senha do +Vendas.")]
    public async Task Put_BlankValues_Returns400WithoutEchoingThem(
        string login, string password, string field, string message)
    {
        var (token, _) = await _fixture.NewSellerAsync();

        var response = await _fixture.SaveCredentialsRawAsync(token, login, password);

        response.StatusCode.Should().Be(400, response.ToString());
        response.As<ErrorResult>().Erros.Should().Contain(e => e.Campo == field && e.Mensagem == message);

        // FR-006 da 006 continua valendo: a recusa não repete o que foi enviado.
        if (password.Length > 0)
        {
            response.Body.Should().NotContain(password);
        }
    }

    [Fact]
    public async Task Put_FirstSaveWithoutPassword_Returns400()
    {
        var (token, _) = await _fixture.NewSellerAsync();

        var response = await _fixture.SaveCredentialsRawAsync(token, LOGIN, null);

        response.StatusCode.Should().Be(400, response.ToString());
        response.As<ErrorResult>().Erros.Should().Contain(e =>
            e.Campo == "password" && e.Mensagem == "Informe a senha junto com o login.");
    }

    [Fact]
    public async Task Put_DifferentLoginWithoutPassword_Returns400()
    {
        var (token, _) = await _fixture.NewSellerAsync();

        await _fixture.SaveCredentialsAsync(token, LOGIN, TestDataHelper.TRACEABLE_PASSWORD);

        var response = await _fixture.SaveCredentialsRawAsync(token, "s999+ok", null);

        response.StatusCode.Should().Be(400, response.ToString());
        response.As<ErrorResult>().Erros.Should().Contain(e =>
            e.Campo == "password" && e.Mensagem == "Informe a senha para trocar o login.");

        // A recusa não mexeu no cadastro: o login continua o primeiro.
        var current = (await _fixture.GetAsync(CREDENTIALS, token)).As<MaisVendasCredentialsInfo>();

        current.Login.Should().Be(LOGIN);
    }

    [Theory]
    [InlineData(LOGIN_MAX + 1, 0, "login", "O login deve ter no máximo 120 caracteres.")]
    [InlineData(0, PASSWORD_MAX + 1, "password", "A senha deve ter no máximo 200 caracteres.")]
    public async Task Put_OverLimit_Returns400(int loginLength, int passwordLength, string field, string message)
    {
        var (token, _) = await _fixture.NewSellerAsync();

        // 0 no comprimento significa "campo de referência": o que o teste quer medir é o excesso do outro.
        var login = loginLength > 0 ? new string('a', loginLength) : LOGIN;
        var password = passwordLength > 0 ? new string('s', passwordLength) : "senha-qualquer";

        var response = await _fixture.SaveCredentialsRawAsync(token, login, password);

        response.StatusCode.Should().Be(400, response.ToString());
        response.As<ErrorResult>().Erros.Should().Contain(e => e.Campo == field && e.Mensagem == message);
    }

    [Fact]
    public async Task Put_EmptyBody_Returns400OnLogin_WithoutCreatingAnything()
    {
        // FR-002: <c>null</c> nos dois campos é "não mexi nisso", e sem cadastro não há o que manter — a
        // recusa cai no campo que falta (login), sem gravar linha nem data. É o corpo que a tela mandaria
        // se alguém habilitasse o botão sem nada digitado, então ele tem de ser recusado no servidor.
        var (token, _) = await _fixture.NewSellerAsync();

        var response = await _fixture.PutAsync(CREDENTIALS, token, new { });

        response.StatusCode.Should().Be(400, response.ToString());
        response.As<ErrorResult>().Erros.Should().ContainSingle(e =>
            e.Campo == "login" && e.Mensagem == "Informe o login do +Vendas.");

        var current = (await _fixture.GetAsync(CREDENTIALS, token)).As<MaisVendasCredentialsInfo>();

        current.Status.Should().Be(MaisVendasCredentialsStatus.None);
        current.HasPassword.Should().BeFalse();
        current.SavedAt.Should().BeNull();
    }

    [Fact]
    public async Task Put_Anonymous_Returns401()
    {
        var response = await _fixture.PutAsync(CREDENTIALS, string.Empty, new { login = LOGIN, password = "senha" });

        response.StatusCode.Should().Be(401, response.Body);
    }

    // =====================================================================================
    // DELETE
    // =====================================================================================

    [Fact]
    public async Task Delete_RemovesCredentialsAndAllowsNewSave()
    {
        var (token, _) = await _fixture.NewSellerAsync();

        await _fixture.SaveCredentialsAsync(token, LOGIN, TestDataHelper.TRACEABLE_PASSWORD);

        var response = await _fixture.DeleteCredentialsAsync(token);

        response.StatusCode.Should().Be(200, response.ToString());

        var info = response.As<MaisVendasCredentialsInfo>();

        info.HasPassword.Should().BeFalse();
        info.Login.Should().BeEmpty();
        info.Status.Should().Be(MaisVendasCredentialsStatus.None);
        info.PasswordChangedAt.Should().BeNull();

        // Recomeço: volta a ser o caminho do primeiro cadastro.
        var again = await _fixture.SaveCredentialsAsync(token, LOGIN, "outra-senha");

        again.StatusCode.Should().Be(200, again.Body);
    }

    [Fact]
    public async Task Delete_WithoutCredentials_IsIdempotent200()
    {
        var (token, _) = await _fixture.NewSellerAsync();

        var first = await _fixture.DeleteCredentialsAsync(token);
        var second = await _fixture.DeleteCredentialsAsync(token);

        first.StatusCode.Should().Be(200, first.Body);
        second.StatusCode.Should().Be(200, second.Body);
        second.As<MaisVendasCredentialsInfo>().HasPassword.Should().BeFalse();
    }

    // =====================================================================================
    // transversal: nada da senha em nenhuma superfície (SC-003)
    // =====================================================================================

    [Fact]
    public async Task PasswordSaved_NeverAppearsInAnyResponse()
    {
        var (token, _) = await _fixture.NewSellerAsync();

        await _fixture.SaveCredentialsAsync(token, LOGIN, TestDataHelper.TRACEABLE_PASSWORD);

        var surfaces = new List<string>
        {
            (await _fixture.GetAsync(CREDENTIALS, token)).Body,
            (await _fixture.GetAsync(DEFAULTS, token)).Body,
            (await _fixture.GetAsync($"{RUNS}?page=1&pageSize=10", token)).Body
        };

        // Execução inexistente: o 404 é superfície também (a mensagem não pode vazar nada).
        surfaces.Add((await _fixture.GetAsync($"{RUNS}/1", token)).Body);

        foreach (var body in surfaces)
        {
            body.Should().NotContain(TestDataHelper.TRACEABLE_PASSWORD);
        }
    }

    [Fact]
    public async Task TwoSellers_DoNotSeeEachOtherCredentials()
    {
        var (tokenA, _) = await _fixture.NewSellerAsync();
        var (tokenB, _) = await _fixture.NewSellerAsync();

        await _fixture.SaveCredentialsAsync(tokenA, LOGIN, TestDataHelper.TRACEABLE_PASSWORD);

        var other = (await _fixture.GetAsync(CREDENTIALS, tokenB)).As<MaisVendasCredentialsInfo>();

        other.HasPassword.Should().BeFalse();
        other.Login.Should().BeEmpty();

        // E não dá para sobrescrever: a rota só conhece o sub do token (constituição §VI).
        await _fixture.SaveCredentialsAsync(tokenB, "s777+ok", "senha-da-B");

        var back = (await _fixture.GetAsync(CREDENTIALS, tokenA)).As<MaisVendasCredentialsInfo>();

        back.Login.Should().Be(LOGIN);
    }

    /// <summary>
    /// O corpo tem uma chave <c>password</c>? Lida no JSON bruto porque <c>hasPassword</c> e
    /// <c>passwordChangedAt</c> contêm "Password" no nome — procurar a string no corpo daria falso
    /// positivo, e a garantia (FR-003) é sobre a <b>chave</b>, não sobre o texto.
    /// </summary>
    private static bool HasPasswordProperty(string body)
    {
        using var document = JsonDocument.Parse(body);

        return document.RootElement.ValueKind == JsonValueKind.Object
            && document.RootElement.TryGetProperty("password", out _);
    }
}
