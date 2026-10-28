using System.Text.Json;
using Ambrosio.ApiTests.Fixtures;
using FluentAssertions;
using Flurl;
using Flurl.Http;

namespace Ambrosio.ApiTests.McpServer;

/// <summary>
/// T029 [US1]: descoberta OAuth anônima (contracts/oauth-endpoints.md §1). O ChatGPT consulta estes
/// documentos antes de ter qualquer credencial; um campo errado aqui impede a conexão inteira.
/// </summary>
[Collection(ApiTestCollection.API_TESTS)]
public class DiscoveryTests(ApiTestFixture fixture)
{
    [Theory]
    [InlineData("/.well-known/oauth-protected-resource/mcp")]
    [InlineData("/.well-known/oauth-protected-resource")]
    public async Task ProtectedResourceMetadata_ShouldDescribeTheMcpResource(string path)
    {
        using var json = await GetJsonAsync($"{Origin}{path}");
        var root = json.RootElement;

        root.GetProperty("resource").GetString().Should().Be(fixture.McpResourceUrl);
        root.GetProperty("authorization_servers").EnumerateArray().Select(e => e.GetString())
            .Should().Contain(fixture.AuthIssuer);
        root.GetProperty("scopes_supported").EnumerateArray().Select(e => e.GetString())
            .Should().Contain(["openid", "customers:read", "customers:write"]);
    }

    [Fact]
    public async Task AuthorizationServerMetadata_ShouldBeTheSameDocumentOnBothAddresses()
    {
        var rfc8414 = await new Url($"{Origin}/.well-known/oauth-authorization-server{new Uri(fixture.AuthIssuer).AbsolutePath}")
            .WithTimeout(fixture.TimeoutSeconds).GetStringAsync();
        var oidc = await new Url($"{fixture.AuthIssuer}/.well-known/openid-configuration")
            .WithTimeout(fixture.TimeoutSeconds).GetStringAsync();

        JsonDocument.Parse(rfc8414).RootElement.GetRawText()
            .Should().Be(JsonDocument.Parse(oidc).RootElement.GetRawText());
    }

    [Fact]
    public async Task AuthorizationServerMetadata_ShouldAdvertiseWhatChatGptRequires()
    {
        using var json = await GetJsonAsync($"{fixture.AuthIssuer}/.well-known/openid-configuration");
        var root = json.RootElement;

        root.GetProperty("issuer").GetString().Should().Be(fixture.AuthIssuer);
        root.GetProperty("authorization_endpoint").GetString().Should().Be($"{fixture.AuthIssuer}/connect/authorize");
        root.GetProperty("token_endpoint").GetString().Should().Be($"{fixture.AuthIssuer}/connect/token");
        root.GetProperty("registration_endpoint").GetString().Should().Be($"{fixture.AuthIssuer}/connect/register");
        root.GetProperty("code_challenge_methods_supported").EnumerateArray().Select(e => e.GetString())
            .Should().Equal("S256");
        root.GetProperty("client_id_metadata_document_supported").GetBoolean().Should().BeTrue();
        root.GetProperty("authorization_response_iss_parameter_supported").GetBoolean().Should().BeTrue();
        root.GetProperty("token_endpoint_auth_methods_supported").EnumerateArray().Select(e => e.GetString())
            .Should().Contain("none").And.NotContain(m => m!.StartsWith("client_secret"));
    }

    [Fact]
    public async Task Jwks_ShouldPublishAnRsaSigningKey()
    {
        using var json = await GetJsonAsync($"{fixture.AuthIssuer}/.well-known/jwks");
        var keys = json.RootElement.GetProperty("keys").EnumerateArray().ToList();

        keys.Should().NotBeEmpty();
        keys.Where(k =>
                k.GetProperty("kty").GetString() == "RSA"
                && k.GetProperty("use").GetString() == "sig"
                && k.TryGetProperty("n", out _)
                && k.TryGetProperty("e", out _))
            .Should().NotBeEmpty();
        keys.Where(k => k.TryGetProperty("d", out _))
            .Should().BeEmpty("a chave privada nunca sai no JWKS");
    }

    private string Origin => new Uri(fixture.McpBaseUrl).GetLeftPart(UriPartial.Authority);

    private async Task<JsonDocument> GetJsonAsync(string url)
    {
        var response = await new Url(url).WithTimeout(fixture.TimeoutSeconds).AllowAnyHttpStatus().GetAsync();
        response.StatusCode.Should().Be(200, $"GET {url} é anônimo");
        response.Headers.TryGetFirst("Content-Type", out var contentType);
        contentType.Should().StartWith("application/json");

        return JsonDocument.Parse(await response.GetStringAsync());
    }
}
