using System.Text.Json;
using Ambrosio.ApiTests.Fixtures;
using Ambrosio.ApiTests.Helpers;
using FluentAssertions;

namespace Ambrosio.ApiTests.McpServer;

/// <summary>
/// T034 [US1]: registro do cliente (contracts/oauth-endpoints.md §2) — DCR só com redirect URI da
/// lista e só cliente público; CIMD recusado para host fora da lista ou que resolva para IP privado.
/// </summary>
[Collection(ApiTestCollection.API_TESTS)]
public class ClientRegistrationTests(ApiTestFixture fixture)
{
    [Fact]
    public async Task Dcr_WithRedirectOutsideTheList_ShouldReturnInvalidRedirectUri()
    {
        var step = await new OAuthFlowHelper(fixture).RegisterRawAsync(new
        {
            redirect_uris = new[] { "https://evil.example.com/callback" }
        });

        step.StatusCode.Should().Be(400);
        ErrorOf(step.Body).Should().Be("invalid_redirect_uri");
    }

    [Theory]
    [InlineData("https://chatgpt.com/connector/oauth/abc/../../roubar")]
    [InlineData("https://chatgpt.com/connector/oauth/abc?next=https://evil.example.com")]
    public async Task Dcr_WithCallbackPatternAbuse_ShouldReturnInvalidRedirectUri(string redirectUri)
    {
        var step = await new OAuthFlowHelper(fixture).RegisterRawAsync(new { redirect_uris = new[] { redirectUri } });

        step.StatusCode.Should().Be(400);
        ErrorOf(step.Body).Should().Be("invalid_redirect_uri");
    }

    [Fact]
    public async Task Dcr_WithConfidentialClient_ShouldBeRefused()
    {
        var step = await new OAuthFlowHelper(fixture).RegisterRawAsync(new
        {
            redirect_uris = new[] { fixture.AuthTestRedirectUri },
            token_endpoint_auth_method = "client_secret_basic"
        });

        step.StatusCode.Should().Be(400);
        ErrorOf(step.Body).Should().Be("invalid_client_metadata");
    }

    [Fact]
    public async Task Dcr_Valid_ShouldCreateAPublicClientThatNeverExpires()
    {
        var step = await new OAuthFlowHelper(fixture).RegisterRawAsync(new
        {
            redirect_uris = new[] { fixture.AuthTestRedirectUri },
            client_name = "ChatGPT de teste"
        });

        step.StatusCode.Should().Be(201, step.Body);
        using var json = JsonDocument.Parse(step.Body);
        var root = json.RootElement;

        root.GetProperty("client_id").GetString().Should().NotBeNullOrEmpty();
        root.GetProperty("client_id_issued_at").GetInt64().Should().BePositive();
        root.GetProperty("token_endpoint_auth_method").GetString().Should().Be("none");
        root.GetProperty("redirect_uris").EnumerateArray().Select(u => u.GetString())
            .Should().Equal(fixture.AuthTestRedirectUri);
        root.TryGetProperty("client_secret", out _).Should().BeFalse();

        if (root.TryGetProperty("client_secret_expires_at", out var expires))
        {
            expires.GetInt64().Should().Be(0);
        }
    }

    [Theory]
    [InlineData("https://evil.example.com/oauth/client.json")]
    [InlineData("https://localhost/oauth/client.json")]
    [InlineData("https://127.0.0.1/oauth/client.json")]
    public async Task Cimd_FromUnlistedHostOrPrivateAddress_ShouldBeRefused(string clientId)
    {
        var oauth = new OAuthFlowHelper(fixture);
        var (_, challenge) = OAuthFlowHelper.CreatePkce();

        var step = await oauth.GetAsync(oauth.AuthorizeUrl(clientId, challenge));

        // Cliente desconhecido: a redirect_uri não é confiável, então o erro não vai para ela. O
        // OpenIddict responde invalid_client com 401.
        step.StatusCode.Should().Be(401);
        step.Location.Should().BeNull();
        step.Body.Should().Contain("invalid_client");
    }

    private static string? ErrorOf(string body)
    {
        using var json = JsonDocument.Parse(body);

        return json.RootElement.GetProperty("error").GetString();
    }
}
