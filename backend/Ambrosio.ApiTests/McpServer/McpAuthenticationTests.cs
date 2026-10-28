using System.Security.Cryptography;
using System.Text.Json;
using Ambrosio.ApiTests.Fixtures;
using Ambrosio.ApiTests.Helpers;
using FluentAssertions;
using Flurl.Http;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Ambrosio.ApiTests.McpServer;

/// <summary>
/// T033 [US1]: autenticação do <c>/mcp</c> (R23). Sem token ou com token inválido → HTTP 401 com
/// <c>resource_metadata</c>; com token, listagem e tools funcionam.
/// </summary>
/// <remarks>
/// Token expirado e token com <c>aud</c> errado assinados pela chave do servidor não são produzíveis
/// de fora: o primeiro exigiria esperar 30 minutos e o segundo, a chave privada. O caminho de
/// validação é o mesmo do token assinado por outra chave, coberto abaixo.
/// </remarks>
[Collection(ApiTestCollection.API_TESTS)]
public class McpAuthenticationTests(ApiTestFixture fixture)
{
    public static TheoryData<string> InvalidTokenKinds => ["adulterado", "outra-chave", "hs256-da-api", "ilegivel"];

    [Theory]
    [MemberData(nameof(InvalidTokenKinds))]
    public async Task InvalidToken_ShouldReturn401WithResourceMetadata(string kind)
    {
        var token = kind switch
        {
            "adulterado" => JwtHelpers.TamperSignature(await ValidAccessTokenAsync()),
            "outra-chave" => ForeignRsaToken(),
            "hs256-da-api" => fixture.TokenA,
            _ => "isto-nao-e-um-jwt"
        };

        var result = await new McpTestClient(fixture).SendWithRawAuthorizationAsync("tools/list", new { }, $"Bearer {token}");

        result.StatusCode.Should().Be(401);
        result.WwwAuthenticate.Should().Contain("resource_metadata=\"")
            .And.Contain("/.well-known/oauth-protected-resource")
            .And.Contain("error=\"invalid_token\"");
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("GET")]
    public async Task WithoutToken_ShouldReturn401WithTheChallengeThatStartsOAuth(string method)
    {
        var response = await new Flurl.Url($"{fixture.McpBaseUrl.TrimEnd('/')}/mcp")
            .WithHeader("Accept", "application/json, text/event-stream")
            .AllowAnyHttpStatus()
            .SendJsonAsync(new HttpMethod(method), method == "POST" ? new { jsonrpc = "2.0", id = 1, method = "tools/list" } : null);

        response.StatusCode.Should().Be(401, "sem o 401 com o desafio o ChatGPT não inicia o login (R23)");
        response.Headers.TryGetFirst("WWW-Authenticate", out var challenge).Should().BeTrue();
        challenge.Should().Contain("resource_metadata=\"")
            .And.Contain("/.well-known/oauth-protected-resource")
            .And.Contain("scope=\"")
            .And.Contain("customers:read")
            .And.NotContain("error=", "requisição sem credencial não leva código de erro (RFC 6750 §3)");
    }

    [Fact]
    public async Task ToolsList_WithToken_ShouldListToolsWithSecuritySchemes()
    {
        var seller = await McpSeller.CreateAsync(fixture);
        var result = await seller.Mcp.ListToolsAsync(seller.McpToken);

        result.StatusCode.Should().Be(200);
        var tools = result.Result!.Value.GetProperty("tools").EnumerateArray().ToList();

        var profile = tools.Single(t => t.GetProperty("name").GetString() == "perfil_ambrosio");
        profile.GetProperty("securitySchemes")[0].GetProperty("type").GetString().Should().Be("oauth2");
        profile.GetProperty("_meta").GetProperty("openai/profile").GetBoolean().Should().BeTrue();
        profile.GetProperty("outputSchema").GetProperty("required").EnumerateArray()
            .Select(r => r.GetString()).Should().Contain("id");

        tools.Select(t => t.GetProperty("securitySchemes")[0].GetProperty("type").GetString())
            .Should().OnlyContain(type => type == "oauth2", "o /mcp exige token, então nenhuma tool é anônima");
    }

    [Fact]
    public async Task SobreAmbrosio_ShouldWork()
    {
        var seller = await McpSeller.CreateAsync(fixture);
        var result = await seller.CallAsync("sobre_ambrosio");

        result.StatusCode.Should().Be(200);
        result.IsToolError.Should().BeFalse();
        result.Text.Should().Contain("Ambrosio");
        result.StructuredContent!.Value.TryGetProperty("consultado_em", out _).Should().BeTrue();
    }

    [Fact]
    public async Task PerfilAmbrosio_ShouldReturnTheSameIdForTwoTokensOfTheSameUser()
    {
        var user = await McpTestUser.CreateAsync(fixture, "Vendedora Perfil");
        var (_, first) = await new OAuthFlowHelper(fixture).GetTokensAsync(user.Email, user.Password);
        var (_, second) = await new OAuthFlowHelper(fixture).GetTokensAsync(user.Email, user.Password);

        var client = new McpTestClient(fixture);
        var a = await client.CallToolAsync("perfil_ambrosio", token: first.AccessToken);
        var b = await client.CallToolAsync("perfil_ambrosio", token: second.AccessToken);

        a.IsToolError.Should().BeFalse(a.RawBody);
        var profile = a.StructuredContent!.Value;
        profile.GetProperty("id").GetString().Should().Be(user.Id.ToString());
        profile.GetProperty("name").GetString().Should().Be(user.Name);
        profile.GetProperty("email").GetString().Should().Be(user.Email);
        b.StructuredContent!.Value.GetProperty("id").GetString().Should().Be(user.Id.ToString());

        JsonDocument.Parse(a.Text!).RootElement.GetProperty("id").GetString().Should().Be(user.Id.ToString());
    }

    private async Task<string> ValidAccessTokenAsync()
    {
        var user = await McpTestUser.CreateAsync(fixture);
        var (_, tokens) = await new OAuthFlowHelper(fixture).GetTokensAsync(user.Email, user.Password);

        return tokens.AccessToken;
    }

    /// <summary>JWT RS256 com issuer, audiência e claims certos, assinado por uma chave que não é a do AS.</summary>
    private string ForeignRsaToken()
    {
        using var rsa = RSA.Create(2048);
        var credentials = new SigningCredentials(new RsaSecurityKey(rsa.ExportParameters(true)), SecurityAlgorithms.RsaSha256);
        var token = new JwtSecurityToken(
            issuer: fixture.AuthIssuer,
            audience: fixture.McpResourceUrl,
            claims: [new Claim("sub", "1"), new Claim("scope", "openid customers:read")],
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
