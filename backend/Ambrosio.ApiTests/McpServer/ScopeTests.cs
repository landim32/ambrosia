using System.Text.RegularExpressions;
using Ambrosio.ApiTests.Fixtures;
using Ambrosio.ApiTests.Helpers;
using FluentAssertions;

namespace Ambrosio.ApiTests.McpServer;

/// <summary>
/// T060 [US4]: autorização por área (R6). Falta de escopo vira desafio <c>insufficient_scope</c> sem
/// gravar nada; a ampliação pede consentimento só pelo escopo novo e libera a gravação; o
/// <c>tools/list</c> declara os escopos de cada tool.
/// </summary>
[Collection(ApiTestCollection.API_TESTS)]
public class ScopeTests(ApiTestFixture fixture)
{
    private const string ReadOnly = "openid email profile customers:read";

    private const string ReadWrite = "openid email profile customers:read customers:write";

    [Fact]
    public async Task MissingScope_ReturnsInsufficientScope_AndExpansionAsksOnlyForTheNewScope()
    {
        var user = await McpTestUser.CreateAsync(fixture, "Vendedora Escopos");
        var oauth = new OAuthFlowHelper(fixture);
        var clientId = await oauth.GetSharedClientAsync();
        var mcp = new McpTestClient(fixture);
        var cpf = TestDataHelper.GenerateValidCpf();

        // 1. Só leitura: a gravação é desafiada e nada é gravado.
        var readOnly = await oauth.GetTokensForClientAsync(clientId, user.Email, user.Password, ReadOnly);
        var denied = await mcp.CallToolAsync("cadastrar_cliente", new { nome = "Sofia Escopo", cpf, confirmar = true }, readOnly.AccessToken);

        denied.IsToolError.Should().BeTrue();
        denied.ToolChallenge.Should().Contain("error=\"insufficient_scope\"")
            .And.Contain("error_description=")
            .And.Contain("scope=\"customers:write\"");
        (await fixture.CountCustomersAsync(user.ApiToken)).Should().Be(0);

        // 2. Ampliação na mesma sessão: o consentimento marca o que já foi concedido e pede só o novo.
        var expanded = await oauth.GetTokensForClientAsync(clientId, user.Email, user.Password, ReadWrite);
        var consent = oauth.ConsentPages.Last();

        ScopeItem(consent, "customers:read").Should().Contain("já autorizado");
        ScopeItem(consent, "customers:write").Should().NotContain("já autorizado");
        consent.Should().Contain("Ampliar o acesso");

        // 3. Com o escopo novo, grava.
        var created = await mcp.CallToolAsync("cadastrar_cliente", new { nome = "Sofia Escopo", cpf, confirmar = true }, expanded.AccessToken);
        created.IsToolError.Should().BeFalse(created.Text);
        (await fixture.CountCustomersAsync(user.ApiToken)).Should().Be(1);

        // 4. Pedir de novo os mesmos escopos não mostra consentimento (autorização somada).
        var pages = oauth.ConsentPages.Count;
        await oauth.GetTokensForClientAsync(clientId, user.Email, user.Password, ReadOnly);
        oauth.ConsentPages.Count.Should().Be(pages);
    }

    [Fact]
    public async Task ToolsList_DeclaresTheScopesOfEachTool()
    {
        var seller = await McpSeller.CreateAsync(fixture);
        var tools = (await seller.Mcp.ListToolsAsync(seller.McpToken)).Result!.Value.GetProperty("tools").EnumerateArray()
            .ToDictionary(t => t.GetProperty("name").GetString()!, t => t.GetProperty("securitySchemes")[0]);

        Scopes(tools["cadastrar_cliente"]).Should().Equal("customers:write");
        Scopes(tools["historico_cliente"]).Should().Equal("customers:read", "orders:read");
        Scopes(tools["atualizar_pedido"]).Should().Equal("orders:write");
        Scopes(tools["sobre_ambrosio"]).Should().Equal("openid");
        tools.Values.Should().OnlyContain(s => s.GetProperty("scopes").GetArrayLength() > 0);
    }

    private static IEnumerable<string?> Scopes(System.Text.Json.JsonElement scheme) =>
        scheme.GetProperty("scopes").EnumerateArray().Select(s => s.GetString());

    /// <summary>O <c>li</c> da tela de consentimento de um escopo.</summary>
    private static string ScopeItem(string html, string scope) =>
        Regex.Match(html, $"<li[^>]*data-scope=\"{Regex.Escape(scope)}\"[^>]*>.*?</li>", RegexOptions.Singleline).Value
            is { Length: > 0 } item
            ? item
            : throw new InvalidOperationException($"Escopo {scope} não aparece no consentimento.");
}
