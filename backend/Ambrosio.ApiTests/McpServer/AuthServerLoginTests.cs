using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ambrosio.ApiTests.Fixtures;
using Ambrosio.ApiTests.Helpers;
using FluentAssertions;
using Flurl;
using Flurl.Http;

namespace Ambrosio.ApiTests.McpServer;

/// <summary>
/// T032 [US1]: tela de login do authorization server (contracts §3, "Telas") — mesma mensagem para
/// senha errada e e-mail inexistente, mesma conta da API e nada de redirect aberto.
/// </summary>
[Collection(ApiTestCollection.API_TESTS)]
public class AuthServerLoginTests(ApiTestFixture fixture)
{
    private static readonly Regex ErrorParagraph = new("<p class=\"error\"[^>]*>(.*?)</p>", RegexOptions.Singleline);

    [Fact]
    public async Task WrongPasswordAndUnknownEmail_ShouldShowTheSameGenericMessage()
    {
        var user = await McpTestUser.CreateAsync(fixture);

        var wrongPassword = await PostLoginAsync(user.Email, "senha-errada-123", returnUrl: null);
        var unknownEmail = await PostLoginAsync($"ninguem+{Guid.NewGuid():N}@exemplo.com", "senha-errada-123", returnUrl: null);

        wrongPassword.StatusCode.Should().Be(200);
        unknownEmail.StatusCode.Should().Be(200);

        var first = ErrorText(wrongPassword.Body);
        first.Should().NotBeNullOrWhiteSpace();
        ErrorText(unknownEmail.Body).Should().Be(first);
    }

    [Fact]
    public async Task UserCreatedByTheApi_ShouldLogInAndKeepTheSameSubject()
    {
        var user = await McpTestUser.CreateAsync(fixture);

        var (_, tokens) = await new OAuthFlowHelper(fixture).GetTokensAsync(user.Email, user.Password);

        using var payload = McpJwt.Claims(tokens.AccessToken);
        payload.RootElement.GetProperty("sub").GetString().Should().Be(user.Id.ToString());
    }

    [Theory]
    [InlineData("https://evil.example.com/roubar")]
    [InlineData("//evil.example.com/auth/connect/authorize")]
    [InlineData("/auth/consent")]
    public async Task ExternalOrUnexpectedReturnUrl_ShouldNotBeFollowed(string returnUrl)
    {
        var user = await McpTestUser.CreateAsync(fixture);

        var step = await PostLoginAsync(user.Email, user.Password, returnUrl);

        step.StatusCode.Should().Be(200, "o login conclui, mas não segue um returnUrl fora do authorize");
        step.Location.Should().BeNull();
    }

    private async Task<OAuthStep> PostLoginAsync(string email, string password, string? returnUrl)
    {
        var jar = new CookieJar();
        var loginUrl = new Url($"{fixture.AuthIssuer}/login");
        if (returnUrl is not null)
        {
            loginUrl.SetQueryParam("returnUrl", returnUrl);
        }

        var page = await loginUrl.Clone().WithCookies(jar).WithTimeout(fixture.TimeoutSeconds).GetStringAsync();
        var fields = OAuthFlowHelper.HiddenFields(page);
        fields["Email"] = email;
        fields["Password"] = password;

        var response = await loginUrl.Clone()
            .WithCookies(jar)
            .WithAutoRedirect(false)
            .WithTimeout(fixture.TimeoutSeconds)
            .AllowAnyHttpStatus()
            .PostUrlEncodedAsync(fields);

        response.Headers.TryGetFirst("Location", out var location);

        return new OAuthStep(response.StatusCode, location, await response.GetStringAsync());
    }

    private static string? ErrorText(string html)
    {
        var match = ErrorParagraph.Match(html);

        return match.Success ? WebUtility.HtmlDecode(match.Groups[1].Value).Trim() : null;
    }
}
