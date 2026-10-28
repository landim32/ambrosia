using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using Ambrosio.ApiTests.Fixtures;
using Flurl;
using Flurl.Http;

namespace Ambrosio.ApiTests.Helpers;

/// <summary>Tokens devolvidos pelo <c>POST /auth/connect/token</c>.</summary>
public sealed record OAuthTokens(string AccessToken, string? RefreshToken, string? IdToken, string Scope, int ExpiresIn);

/// <summary>Resposta de uma chamada ao AS, sem seguir redirect.</summary>
public sealed record OAuthStep(int StatusCode, string? Location, string Body)
{
    /// <summary>Parâmetros da query do <c>Location</c> (code, state, iss, error...).</summary>
    public IReadOnlyDictionary<string, string> LocationQuery
    {
        get
        {
            if (Location is null || !Location.Contains('?'))
            {
                return new Dictionary<string, string>();
            }

            var query = HttpUtility.ParseQueryString(Location[(Location.IndexOf('?') + 1)..]);

            return query.AllKeys
                .Where(k => k is not null)
                .ToDictionary(k => k!, k => query[k] ?? string.Empty);
        }
    }
}

/// <summary>
/// Fluxo OAuth completo por HTTP contra o authorization server (feature 007, T027): registra um
/// cliente por DCR, gera PKCE S256, passa pelo login (<c>/auth/login</c>, com cookie e antiforgery) e
/// pelo consentimento (<c>/auth/consent</c>), lê o <c>code</c> e o <c>iss</c> do <c>Location</c> e troca
/// no token. Cada instância tem o próprio cookie jar: é uma "aba de navegador".
/// </summary>
public sealed class OAuthFlowHelper(ApiTestFixture fixture)
{
    /// <summary>Escopos pedidos por padrão: identidade + leitura de clientes.</summary>
    public const string DefaultScopes = "openid email profile offline_access customers:read";

    private static readonly Regex HiddenInput = new(
        "<input[^>]*type=\"hidden\"[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Attribute = new(
        "(name|value)=\"([^\"]*)\"", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public CookieJar Cookies { get; private set; } = new();

    public string Issuer => fixture.AuthIssuer;

    /// <summary>Começa uma sessão de navegador nova (sem login no AS).</summary>
    public void ResetSession() => Cookies = new CookieJar();

    // ------------------------------------------------------------------ registro e PKCE

    /// <summary><c>POST /auth/connect/register</c> com a redirect URI de teste; devolve o client_id.</summary>
    public async Task<string> RegisterClientAsync(string? redirectUri = null)
    {
        var response = await RegisterRawAsync(new
        {
            redirect_uris = new[] { redirectUri ?? fixture.AuthTestRedirectUri },
            client_name = "ApiTests",
            token_endpoint_auth_method = "none",
            grant_types = new[] { "authorization_code", "refresh_token" },
            response_types = new[] { "code" }
        });

        if (response.StatusCode != 201)
        {
            throw new InvalidOperationException($"DCR respondeu {response.StatusCode}: {response.Body}");
        }

        using var json = JsonDocument.Parse(response.Body);

        return json.RootElement.GetProperty("client_id").GetString()!;
    }

    public async Task<OAuthStep> RegisterRawAsync(object body)
    {
        var response = await new Url($"{Issuer}/connect/register")
            .WithTimeout(fixture.TimeoutSeconds)
            .AllowAnyHttpStatus()
            .PostJsonAsync(body);

        return await ToStep(response);
    }

    public static (string Verifier, string Challenge) CreatePkce()
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

        return (verifier, challenge);
    }

    // ------------------------------------------------------------------ authorize

    /// <summary>
    /// URL do authorize. Os parâmetros com <c>null</c> ficam de fora (para testar ausência);
    /// <paramref name="resource"/> vazio usa o recurso do ambiente.
    /// </summary>
    public string AuthorizeUrl(
        string clientId,
        string? codeChallenge,
        string scopes = DefaultScopes,
        string? state = "estado-de-teste",
        string? resource = "",
        string? codeChallengeMethod = "S256",
        string? redirectUri = null)
    {
        var url = new Url($"{Issuer}/connect/authorize")
            .SetQueryParam("response_type", "code")
            .SetQueryParam("client_id", clientId)
            .SetQueryParam("redirect_uri", redirectUri ?? fixture.AuthTestRedirectUri)
            .SetQueryParam("scope", scopes);

        if (state is not null)
        {
            url.SetQueryParam("state", state);
        }

        if (codeChallenge is not null)
        {
            url.SetQueryParam("code_challenge", codeChallenge);
        }

        if (codeChallengeMethod is not null)
        {
            url.SetQueryParam("code_challenge_method", codeChallengeMethod);
        }

        if (resource is not null)
        {
            url.SetQueryParam("resource", resource.Length == 0 ? fixture.McpResourceUrl : resource);
        }

        return url.ToString();
    }

    public async Task<OAuthStep> GetAsync(string url) =>
        await ToStep(await Request(url).GetAsync());

    /// <summary>
    /// Leva o authorize até a <c>redirect_uri</c>: faz login se pedir e decide o consentimento
    /// (<paramref name="consent"/> = accept/deny) se aparecer. Devolve o último redirect, que aponta
    /// para a <c>redirect_uri</c> com <c>code</c> ou <c>error</c>.
    /// </summary>
    public async Task<OAuthStep> AuthorizeAsync(string authorizeUrl, string email, string password, string consent = "accept")
    {
        var step = await GetAsync(authorizeUrl);

        for (var hop = 0; hop < 6; hop++)
        {
            if (step.StatusCode != 302 || step.Location is null)
            {
                return step;
            }

            var location = Absolute(step.Location);

            if (location.Contains("/auth/login", StringComparison.Ordinal))
            {
                step = await LoginAsync(location, email, password);
                continue;
            }

            if (location.Contains("/auth/consent", StringComparison.Ordinal))
            {
                step = await ConsentAsync(location, consent);
                continue;
            }

            if (location.Contains("/connect/authorize", StringComparison.Ordinal))
            {
                step = await GetAsync(location);
                continue;
            }

            // Fora do AS: é a redirect_uri do cliente.
            return step;
        }

        throw new InvalidOperationException("O authorize não chegou à redirect_uri em 6 passos.");
    }

    /// <summary>GET na tela de login e POST do formulário (e-mail e senha, com antiforgery).</summary>
    public async Task<OAuthStep> LoginAsync(string loginUrl, string email, string password)
    {
        var page = await GetAsync(loginUrl);
        var fields = HiddenFields(page.Body);
        fields["Email"] = email;
        fields["Password"] = password;

        var response = await Request(loginUrl).PostUrlEncodedAsync(fields);

        return await ToStep(response);
    }

    /// <summary>GET no consentimento e POST da decisão para o authorize.</summary>
    public async Task<OAuthStep> ConsentAsync(string consentUrl, string decision)
    {
        var page = await GetAsync(consentUrl);
        if (page.StatusCode != 200)
        {
            return page;
        }

        var fields = HiddenFields(page.Body);
        fields["decision"] = decision;

        var response = await Request($"{Issuer}/connect/authorize").PostUrlEncodedAsync(fields);

        return await ToStep(response);
    }

    // ------------------------------------------------------------------ token

    public async Task<OAuthStep> TokenRawAsync(IDictionary<string, string?> form)
    {
        var response = await new Url($"{Issuer}/connect/token")
            .WithTimeout(fixture.TimeoutSeconds)
            .AllowAnyHttpStatus()
            .PostUrlEncodedAsync(form.Where(p => p.Value is not null).ToDictionary(p => p.Key, p => p.Value!));

        return await ToStep(response);
    }

    public async Task<OAuthTokens> ExchangeCodeAsync(string clientId, string code, string verifier)
    {
        var step = await TokenRawAsync(new Dictionary<string, string?>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["code_verifier"] = verifier,
            ["redirect_uri"] = fixture.AuthTestRedirectUri,
            ["client_id"] = clientId,
            ["resource"] = fixture.McpResourceUrl
        });

        return ParseTokens(step);
    }

    public async Task<OAuthStep> RefreshRawAsync(string clientId, string refreshToken) =>
        await TokenRawAsync(new Dictionary<string, string?>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = clientId,
            ["resource"] = fixture.McpResourceUrl
        });

    /// <summary>Fluxo inteiro, do DCR ao token, para um usuário já cadastrado.</summary>
    public async Task<(string ClientId, OAuthTokens Tokens)> GetTokensAsync(
        string email,
        string password,
        string scopes = DefaultScopes)
    {
        var clientId = await RegisterClientAsync();
        var tokens = await GetTokensForClientAsync(clientId, email, password, scopes);

        return (clientId, tokens);
    }

    public async Task<OAuthTokens> GetTokensForClientAsync(string clientId, string email, string password, string scopes = DefaultScopes)
    {
        var (verifier, challenge) = CreatePkce();
        var step = await AuthorizeAsync(AuthorizeUrl(clientId, challenge, scopes), email, password);

        if (!step.LocationQuery.TryGetValue("code", out var code))
        {
            throw new InvalidOperationException(
                $"O authorize não devolveu código (status {step.StatusCode}, Location {step.Location}): {step.Body}");
        }

        return await ExchangeCodeAsync(clientId, code, verifier);
    }

    public static OAuthTokens ParseTokens(OAuthStep step)
    {
        if (step.StatusCode != 200)
        {
            throw new InvalidOperationException($"Token respondeu {step.StatusCode}: {step.Body}");
        }

        using var json = JsonDocument.Parse(step.Body);
        var root = json.RootElement;

        return new OAuthTokens(
            root.GetProperty("access_token").GetString()!,
            root.TryGetProperty("refresh_token", out var refresh) ? refresh.GetString() : null,
            root.TryGetProperty("id_token", out var id) ? id.GetString() : null,
            root.TryGetProperty("scope", out var scope) ? scope.GetString() ?? string.Empty : string.Empty,
            root.TryGetProperty("expires_in", out var expires) ? expires.GetInt32() : 0);
    }

    // ------------------------------------------------------------------ util

    /// <summary>Campos <c>input type="hidden"</c> do HTML (antiforgery, ReturnUrl, parâmetros OAuth).</summary>
    public static Dictionary<string, string> HiddenFields(string html)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (Match input in HiddenInput.Matches(html))
        {
            string? name = null;
            var value = string.Empty;

            foreach (Match attribute in Attribute.Matches(input.Value))
            {
                if (attribute.Groups[1].Value.Equals("name", StringComparison.OrdinalIgnoreCase))
                {
                    name = WebUtility.HtmlDecode(attribute.Groups[2].Value);
                }
                else
                {
                    value = WebUtility.HtmlDecode(attribute.Groups[2].Value);
                }
            }

            if (!string.IsNullOrEmpty(name))
            {
                fields[name] = value;
            }
        }

        return fields;
    }

    private IFlurlRequest Request(string url) =>
        new Url(url)
            .WithTimeout(fixture.TimeoutSeconds)
            .WithCookies(Cookies)
            .WithAutoRedirect(false)
            .AllowAnyHttpStatus();

    private string Absolute(string location) =>
        Uri.TryCreate(location, UriKind.Absolute, out var absolute)
            ? absolute.ToString()
            : new Uri(new Uri(fixture.McpBaseUrl), location).ToString();

    private static async Task<OAuthStep> ToStep(IFlurlResponse response)
    {
        response.Headers.TryGetFirst("Location", out var location);

        return new OAuthStep(response.StatusCode, location, await response.GetStringAsync());
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
