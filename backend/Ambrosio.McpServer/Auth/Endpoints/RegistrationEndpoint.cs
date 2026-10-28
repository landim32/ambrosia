using System.Text.Json;
using System.Text.Json.Serialization;
using Ambrosio.DTO.Settings;
using Ambrosio.McpServer.Middlewares;
using Microsoft.Extensions.Options;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Ambrosio.McpServer.Auth.Endpoints;

/// <summary>
/// Registro dinâmico de cliente, RFC 7591 (feature 007, T038, contracts/oauth-endpoints.md §2):
/// <c>POST /auth/connect/register</c>. Cria só cliente público (<c>none</c>), sem segredo e sem
/// expiração, com redirect URIs da lista <c>Auth:AllowedRedirectUris</c>.
/// </summary>
public static class RegistrationEndpoint
{
    /// <summary>Relativo ao PathBase <c>/auth</c>.</summary>
    public const string Path = "/connect/register";

    public static IEndpointRouteBuilder MapAmbrosioRegistrationEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(Path, HandleAsync)
            .AllowAnonymous()
            .DisableAntiforgery()
            .RequireRateLimiting(McpRateLimiter.RegistrationPolicyName);

        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        ClientRegistrar registrar,
        IOptions<AuthServerSettings> settings,
        CancellationToken cancellationToken)
    {
        RegistrationRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync<RegistrationRequest>(context.Request.Body, cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            return Error("invalid_client_metadata", "O corpo precisa ser um JSON de metadados do cliente.");
        }

        if (request?.RedirectUris is not { Count: > 0 } redirectUris)
        {
            return Error("invalid_redirect_uri", "redirect_uris é obrigatório.");
        }

        if (redirectUris.Any(uri => !RedirectUriPolicy.IsAllowed(uri, settings.Value.AllowedRedirectUris)))
        {
            return Error("invalid_redirect_uri", "Uma das redirect_uris não é permitida por este servidor.");
        }

        if (request.TokenEndpointAuthMethod is { } method && method != ClientAuthenticationMethods.None)
        {
            return Error("invalid_client_metadata", "Só clientes públicos (token_endpoint_auth_method none) são aceitos.");
        }

        if (request.GrantTypes?.Any(g => g is not GrantTypes.AuthorizationCode and not GrantTypes.RefreshToken) == true)
        {
            return Error("invalid_client_metadata", "Só os grants authorization_code e refresh_token são aceitos.");
        }

        if (request.ResponseTypes?.Any(r => r != ResponseTypes.Code) == true)
        {
            return Error("invalid_client_metadata", "Só o response_type code é aceito.");
        }

        var clientName = request.ClientName is { Length: > 200 } name ? name[..200] : request.ClientName;
        var clientId = $"dcr_{Guid.NewGuid():N}";
        var uris = redirectUris.Distinct(StringComparer.Ordinal).ToList();

        var application = await registrar.CreateAsync(clientId, clientName, uris, ClientRegistrar.SourceDcr, cancellationToken);

        return Results.Json(
            new RegistrationResponse
            {
                ClientId = clientId,
                ClientIdIssuedAt = new DateTimeOffset(application.CreatedAt, TimeSpan.Zero).ToUnixTimeSeconds(),
                ClientName = clientName,
                RedirectUris = uris,
                GrantTypes = [GrantTypes.AuthorizationCode, GrantTypes.RefreshToken],
                ResponseTypes = [ResponseTypes.Code],
                TokenEndpointAuthMethod = ClientAuthenticationMethods.None
            },
            statusCode: StatusCodes.Status201Created);
    }

    private static IResult Error(string error, string description) =>
        Results.Json(new { error, error_description = description }, statusCode: StatusCodes.Status400BadRequest);

    private sealed class RegistrationRequest
    {
        [JsonPropertyName("redirect_uris")]
        public List<string>? RedirectUris { get; set; }

        [JsonPropertyName("client_name")]
        public string? ClientName { get; set; }

        [JsonPropertyName("token_endpoint_auth_method")]
        public string? TokenEndpointAuthMethod { get; set; }

        [JsonPropertyName("grant_types")]
        public List<string>? GrantTypes { get; set; }

        [JsonPropertyName("response_types")]
        public List<string>? ResponseTypes { get; set; }
    }

    private sealed class RegistrationResponse
    {
        [JsonPropertyName("client_id")]
        public string ClientId { get; set; } = string.Empty;

        [JsonPropertyName("client_id_issued_at")]
        public long ClientIdIssuedAt { get; set; }

        [JsonPropertyName("client_name")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ClientName { get; set; }

        [JsonPropertyName("redirect_uris")]
        public List<string> RedirectUris { get; set; } = [];

        [JsonPropertyName("grant_types")]
        public List<string> GrantTypes { get; set; } = [];

        [JsonPropertyName("response_types")]
        public List<string> ResponseTypes { get; set; } = [];

        [JsonPropertyName("token_endpoint_auth_method")]
        public string TokenEndpointAuthMethod { get; set; } = string.Empty;
    }
}
