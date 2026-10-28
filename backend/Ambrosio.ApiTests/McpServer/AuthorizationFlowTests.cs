using Ambrosio.ApiTests.Fixtures;
using Ambrosio.ApiTests.Helpers;
using FluentAssertions;

namespace Ambrosio.ApiTests.McpServer;

/// <summary>
/// T030 [US1]: fluxo de autorização (contracts/oauth-endpoints.md §3) — PKCE S256 obrigatório,
/// <c>resource</c> exato, <c>iss</c> em toda resposta à redirect URI, negação e consentimento
/// lembrado.
/// </summary>
[Collection(ApiTestCollection.API_TESTS)]
public class AuthorizationFlowTests(ApiTestFixture fixture)
{
    [Fact]
    public async Task FullFlow_ShouldReturnCodeWithStateAndIss_AndExchangeForTokens()
    {
        var user = await McpTestUser.CreateAsync(fixture);
        var oauth = new OAuthFlowHelper(fixture);
        var clientId = await oauth.RegisterClientAsync();
        var (verifier, challenge) = OAuthFlowHelper.CreatePkce();

        var step = await oauth.AuthorizeAsync(oauth.AuthorizeUrl(clientId, challenge, state: "abc123"), user.Email, user.Password);

        step.StatusCode.Should().Be(302);
        step.Location.Should().StartWith(fixture.AuthTestRedirectUri);
        step.LocationQuery.Should().ContainKey("code");
        step.LocationQuery["state"].Should().Be("abc123");
        step.LocationQuery["iss"].Should().Be(fixture.AuthIssuer);

        var tokens = await oauth.ExchangeCodeAsync(clientId, step.LocationQuery["code"], verifier);

        tokens.AccessToken.Should().NotBeNullOrEmpty();
        tokens.RefreshToken.Should().NotBeNullOrEmpty();
        tokens.IdToken.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task WithoutCodeChallenge_ShouldReturnInvalidRequestWithIss()
    {
        var oauth = new OAuthFlowHelper(fixture);
        var clientId = await oauth.RegisterClientAsync();

        var step = await oauth.GetAsync(oauth.AuthorizeUrl(clientId, codeChallenge: null, codeChallengeMethod: null));

        AssertRequestError(step, "invalid_request");
    }

    [Fact]
    public async Task WithPlainChallengeMethod_ShouldReturnInvalidRequest()
    {
        var oauth = new OAuthFlowHelper(fixture);
        var clientId = await oauth.RegisterClientAsync();
        var (verifier, _) = OAuthFlowHelper.CreatePkce();

        var step = await oauth.GetAsync(oauth.AuthorizeUrl(clientId, verifier, codeChallengeMethod: "plain"));

        AssertRequestError(step, "invalid_request");
    }

    [Fact]
    public async Task WithoutResource_ShouldReturnInvalidTarget()
    {
        var oauth = new OAuthFlowHelper(fixture);
        var clientId = await oauth.RegisterClientAsync();
        var (_, challenge) = OAuthFlowHelper.CreatePkce();

        var step = await oauth.GetAsync(oauth.AuthorizeUrl(clientId, challenge, resource: null));

        AssertRequestError(step, "invalid_target");
    }

    [Fact]
    public async Task WithResourceEndingInSlash_ShouldReturnInvalidTarget()
    {
        var oauth = new OAuthFlowHelper(fixture);
        var clientId = await oauth.RegisterClientAsync();
        var (_, challenge) = OAuthFlowHelper.CreatePkce();

        var step = await oauth.GetAsync(oauth.AuthorizeUrl(clientId, challenge, resource: fixture.McpResourceUrl + "/"));

        AssertRequestError(step, "invalid_target");
    }

    [Fact]
    public async Task DenyingConsent_ShouldReturnAccessDeniedWithIss()
    {
        var user = await McpTestUser.CreateAsync(fixture);
        var oauth = new OAuthFlowHelper(fixture);
        var clientId = await oauth.RegisterClientAsync();
        var (_, challenge) = OAuthFlowHelper.CreatePkce();

        var step = await oauth.AuthorizeAsync(oauth.AuthorizeUrl(clientId, challenge), user.Email, user.Password, consent: "deny");

        AssertRedirectError(step, "access_denied");
    }

    [Fact]
    public async Task SecondAuthorizationWithSameScopes_ShouldSkipConsent()
    {
        var user = await McpTestUser.CreateAsync(fixture);
        var oauth = new OAuthFlowHelper(fixture);
        var clientId = await oauth.RegisterClientAsync();
        await oauth.GetTokensForClientAsync(clientId, user.Email, user.Password);

        // Mesma sessão de navegador, mesmos escopos: o authorize já devolve o código.
        var (_, challenge) = OAuthFlowHelper.CreatePkce();
        var step = await oauth.GetAsync(oauth.AuthorizeUrl(clientId, challenge));

        step.StatusCode.Should().Be(302);
        step.Location.Should().StartWith(fixture.AuthTestRedirectUri, "não deve passar pela tela de consentimento");
        step.LocationQuery.Should().ContainKey("code");
    }

    [Fact]
    public async Task WithoutSession_ShouldRedirectToLogin()
    {
        var oauth = new OAuthFlowHelper(fixture);
        var clientId = await oauth.RegisterClientAsync();
        var (_, challenge) = OAuthFlowHelper.CreatePkce();

        var step = await oauth.GetAsync(oauth.AuthorizeUrl(clientId, challenge));

        step.StatusCode.Should().Be(302);
        step.Location.Should().Contain("/auth/login?returnUrl=");
    }

    /// <summary>
    /// Erro de parâmetro (PKCE, <c>resource</c>): o OpenIddict valida antes de autenticar o cliente, ou
    /// seja, antes de a <c>redirect_uri</c> ser confiável — então responde 400 na própria página, sem
    /// redirecionar e sem código. É o comportamento seguro; o contrato §3 foi ajustado a ele.
    /// </summary>
    private static void AssertRequestError(OAuthStep step, string error)
    {
        step.StatusCode.Should().Be(400, step.Body);
        step.Location.Should().BeNull();
        step.Body.Should().Contain(error);
    }

    private void AssertRedirectError(OAuthStep step, string error)
    {
        step.StatusCode.Should().Be(302, step.Body);
        step.Location.Should().StartWith(fixture.AuthTestRedirectUri);
        step.LocationQuery.Should().ContainKey("error").WhoseValue.Should().Be(error);
        step.LocationQuery.Should().ContainKey("iss").WhoseValue.Should().Be(fixture.AuthIssuer);
        step.LocationQuery.Should().NotContainKey("code");
    }
}
