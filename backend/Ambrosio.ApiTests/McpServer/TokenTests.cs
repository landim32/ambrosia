using System.Text.Json;
using Ambrosio.ApiTests.Fixtures;
using Ambrosio.ApiTests.Helpers;
using FluentAssertions;
using Flurl;
using Flurl.Http;

namespace Ambrosio.ApiTests.McpServer;

/// <summary>
/// T031 [US1]: tokens do authorization server (contracts/oauth-endpoints.md §4–6) — claims do access
/// token, rotação do refresh com revogação da família no reuso, userinfo e revogação.
/// </summary>
[Collection(ApiTestCollection.API_TESTS)]
public class TokenTests(ApiTestFixture fixture)
{
    [Fact]
    public async Task AccessToken_ShouldCarryIssuerAudienceSubjectScopeAndThirtyMinutes()
    {
        var user = await McpTestUser.CreateAsync(fixture);
        var (_, tokens) = await new OAuthFlowHelper(fixture).GetTokensAsync(user.Email, user.Password);

        using var payload = McpJwt.Claims(tokens.AccessToken);
        var claims = payload.RootElement;

        claims.GetProperty("iss").GetString().Should().Be(fixture.AuthIssuer);
        Audiences(claims).Should().Equal(fixture.McpResourceUrl);
        claims.GetProperty("sub").GetString().Should().Be(user.Id.ToString());
        claims.GetProperty("scope").GetString()!.Split(' ').Should().Contain("customers:read");
        claims.TryGetProperty("email", out _).Should().BeFalse("nome e e-mail vão só no id token");

        var lifetime = claims.GetProperty("exp").GetInt64() - claims.GetProperty("iat").GetInt64();
        lifetime.Should().Be(30 * 60);
        tokens.ExpiresIn.Should().BeInRange(29 * 60, 30 * 60);
    }

    [Fact]
    public async Task Refresh_ShouldRotate_AndReusingTheOldOneShouldRevokeTheFamily()
    {
        var user = await McpTestUser.CreateAsync(fixture);
        var oauth = new OAuthFlowHelper(fixture);
        var (clientId, tokens) = await oauth.GetTokensAsync(user.Email, user.Password);

        var rotated = OAuthFlowHelper.ParseTokens(await oauth.RefreshRawAsync(clientId, tokens.RefreshToken!));
        rotated.RefreshToken.Should().NotBeNullOrEmpty().And.NotBe(tokens.RefreshToken);

        // Reuso do refresh já trocado: recusado...
        var reuse = await oauth.RefreshRawAsync(clientId, tokens.RefreshToken!);
        reuse.StatusCode.Should().Be(400);
        ErrorOf(reuse).Should().Be("invalid_grant");

        // ...e a família inteira cai, inclusive o refresh novo.
        var afterReuse = await oauth.RefreshRawAsync(clientId, rotated.RefreshToken!);
        afterReuse.StatusCode.Should().Be(400);
        ErrorOf(afterReuse).Should().Be("invalid_grant");
    }

    [Fact]
    public async Task UserInfo_ShouldReturnSubjectAndVerifiedEmail()
    {
        var user = await McpTestUser.CreateAsync(fixture, "Vendedora Userinfo");
        var (_, tokens) = await new OAuthFlowHelper(fixture).GetTokensAsync(user.Email, user.Password);

        var response = await new Url($"{fixture.AuthIssuer}/connect/userinfo")
            .WithTimeout(fixture.TimeoutSeconds)
            .WithOAuthBearerToken(tokens.AccessToken)
            .AllowAnyHttpStatus()
            .GetAsync();

        response.StatusCode.Should().Be(200);
        using var json = JsonDocument.Parse(await response.GetStringAsync());
        json.RootElement.GetProperty("sub").GetString().Should().Be(user.Id.ToString());
        json.RootElement.GetProperty("email").GetString().Should().Be(user.Email);
        json.RootElement.GetProperty("email_verified").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("name").GetString().Should().Be(user.Name);
    }

    [Fact]
    public async Task Revoke_ShouldInvalidateTheRefreshToken()
    {
        var user = await McpTestUser.CreateAsync(fixture);
        var oauth = new OAuthFlowHelper(fixture);
        var (clientId, tokens) = await oauth.GetTokensAsync(user.Email, user.Password);

        var revoke = await new Url($"{fixture.AuthIssuer}/connect/revoke")
            .WithTimeout(fixture.TimeoutSeconds)
            .AllowAnyHttpStatus()
            .PostUrlEncodedAsync(new Dictionary<string, string>
            {
                ["token"] = tokens.RefreshToken!,
                ["token_type_hint"] = "refresh_token",
                ["client_id"] = clientId
            });

        revoke.StatusCode.Should().Be(200, await revoke.GetStringAsync());

        var refresh = await oauth.RefreshRawAsync(clientId, tokens.RefreshToken!);
        refresh.StatusCode.Should().Be(400);
        ErrorOf(refresh).Should().Be("invalid_grant");
    }

    private static IEnumerable<string?> Audiences(JsonElement claims)
    {
        var aud = claims.GetProperty("aud");

        return aud.ValueKind == JsonValueKind.Array
            ? aud.EnumerateArray().Select(a => a.GetString()).ToList()
            : [aud.GetString()];
    }

    private static string? ErrorOf(OAuthStep step)
    {
        using var json = JsonDocument.Parse(step.Body);

        return json.RootElement.GetProperty("error").GetString();
    }
}
