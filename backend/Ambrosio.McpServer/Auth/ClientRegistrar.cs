using Ambrosio.Domain.Interfaces;
using Ambrosio.Infra.Context.OAuth;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Ambrosio.McpServer.Auth;

/// <summary>
/// Cria e atualiza os clientes OAuth públicos do ChatGPT (feature 007, R4): um por registro DCR e um
/// por documento CIMD. Todos são públicos (<c>token_endpoint_auth_method: none</c>, PKCE S256
/// obrigatório), com código de autorização e refresh token.
/// </summary>
public sealed class ClientRegistrar(IOpenIddictApplicationManager applications, IClock clock)
{
    /// <summary><c>oauth_applications.registration_source</c>: cliente do documento CIMD.</summary>
    public const int SourceCimd = 1;

    /// <summary><c>oauth_applications.registration_source</c>: cliente do registro dinâmico.</summary>
    public const int SourceDcr = 2;

    public async Task<OAuthApplication> CreateAsync(
        string clientId,
        string? clientName,
        IReadOnlyCollection<string> redirectUris,
        int source,
        CancellationToken cancellationToken)
    {
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = clientId,
            ClientType = ClientTypes.Public,
            ConsentType = ConsentTypes.Explicit,
            DisplayName = string.IsNullOrWhiteSpace(clientName) ? "ChatGPT" : clientName.Trim(),
            Permissions =
            {
                Permissions.Endpoints.Authorization,
                Permissions.Endpoints.Token,
                Permissions.Endpoints.Revocation,
                Permissions.GrantTypes.AuthorizationCode,
                Permissions.GrantTypes.RefreshToken,
                Permissions.ResponseTypes.Code
            },
            Requirements = { Requirements.Features.ProofKeyForCodeExchange }
        };

        foreach (var uri in redirectUris)
        {
            descriptor.RedirectUris.Add(new Uri(uri, UriKind.Absolute));
        }

        var application = (OAuthApplication)await applications.CreateAsync(descriptor, cancellationToken);

        // Colunas que o OpenIddict não conhece (data-model §1.1): gravadas na sequência, pela mesma
        // entidade, para a linha nunca ficar sem origem.
        var now = clock.UtcNow;
        application.RegistrationSource = source;
        application.CreatedAt = now;
        application.UpdatedAt = now;
        application.MetadataFetchedAt = source == SourceCimd ? now : null;
        await applications.UpdateAsync(application, cancellationToken);

        return application;
    }

    /// <summary>
    /// Garante o cliente CIMD com as redirect URIs do documento. Atualiza só quando o conjunto mudou,
    /// para não escrever no banco a cada autorização.
    /// </summary>
    public async Task UpsertCimdAsync(
        string clientId,
        string? clientName,
        IReadOnlyCollection<string> redirectUris,
        CancellationToken cancellationToken)
    {
        if (await applications.FindByClientIdAsync(clientId, cancellationToken) is not OAuthApplication application)
        {
            await CreateAsync(clientId, clientName, redirectUris, SourceCimd, cancellationToken);
            return;
        }

        var current = (await applications.GetRedirectUrisAsync(application, cancellationToken)).ToHashSet(StringComparer.Ordinal);
        if (current.SetEquals(redirectUris))
        {
            return;
        }

        var descriptor = new OpenIddictApplicationDescriptor();
        await applications.PopulateAsync(descriptor, application, cancellationToken);

        descriptor.RedirectUris.Clear();
        foreach (var uri in redirectUris)
        {
            descriptor.RedirectUris.Add(new Uri(uri, UriKind.Absolute));
        }

        await applications.PopulateAsync(application, descriptor, cancellationToken);

        var now = clock.UtcNow;
        application.MetadataFetchedAt = now;
        application.UpdatedAt = now;
        await applications.UpdateAsync(application, cancellationToken);
    }
}
