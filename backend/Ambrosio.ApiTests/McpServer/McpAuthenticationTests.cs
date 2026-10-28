using System.Security.Cryptography;
using System.Text.Json;
using Ambrosio.ApiTests.Fixtures;
using Ambrosio.ApiTests.Helpers;
using FluentAssertions;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Ambrosio.ApiTests.McpServer;

/// <summary>
/// T033 [US1]: autenticação mista do <c>/mcp</c> (R7, contracts/mcp-tools.md "Regras comuns"). Token
/// presente e inválido → HTTP 401 com <c>resource_metadata</c>; sem token, listagem e
/// <c>sobre_ambrosio</c> funcionam e a tool protegida devolve o desafio no resultado.
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

    [Fact]
    public async Task ToolsList_WithoutToken_ShouldListToolsWithSecuritySchemes()
    {
        var result = await new McpTestClient(fixture).ListToolsAsync();

        result.StatusCode.Should().Be(200);
        var tools = result.Result!.Value.GetProperty("tools").EnumerateArray().ToList();

        var profile = tools.Single(t => t.GetProperty("name").GetString() == "perfil_ambrosio");
        profile.GetProperty("securitySchemes")[0].GetProperty("type").GetString().Should().Be("oauth2");
        profile.GetProperty("_meta").GetProperty("openai/profile").GetBoolean().Should().BeTrue();
        profile.GetProperty("outputSchema").GetProperty("required").EnumerateArray()
            .Select(r => r.GetString()).Should().Contain("id");

        var about = tools.Single(t => t.GetProperty("name").GetString() == "sobre_ambrosio");
        about.GetProperty("securitySchemes")[0].GetProperty("type").GetString().Should().Be("noauth");
    }

    [Fact]
    public async Task SobreAmbrosio_WithoutToken_ShouldWork()
    {
        var result = await new McpTestClient(fixture).CallToolAsync("sobre_ambrosio");

        result.StatusCode.Should().Be(200);
        result.IsToolError.Should().BeFalse();
        result.Text.Should().Contain("Ambrosio");
        result.StructuredContent!.Value.TryGetProperty("consultado_em", out _).Should().BeTrue();
    }

    [Fact]
    public async Task PerfilAmbrosio_WithoutToken_ShouldReturnTheChallengeInTheResult()
    {
        var result = await new McpTestClient(fixture).CallToolAsync("perfil_ambrosio");

        result.StatusCode.Should().Be(200);
        result.IsToolError.Should().BeTrue();
        result.ToolChallenge.Should().Contain("error=\"invalid_token\"")
            .And.Contain("error_description=")
            .And.Contain("resource_metadata=")
            .And.Contain("scope=\"openid\"");
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
