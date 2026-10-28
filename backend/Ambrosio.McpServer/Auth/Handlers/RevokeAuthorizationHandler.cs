using OpenIddict.Abstractions;
using OpenIddict.Server;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace Ambrosio.McpServer.Auth.Handlers;

/// <summary>
/// Revogar um refresh token desconecta a conta (feature 007, T044): além do token, o que o OpenIddict
/// já faz, revoga a autorização ligada a ele e os demais tokens dela. Sem isto, desconectar no
/// ChatGPT deixaria o consentimento permanente valendo para o próximo authorize.
/// </summary>
public sealed class RevokeAuthorizationHandler(
    IOpenIddictAuthorizationManager authorizations,
    IOpenIddictTokenManager tokens) : IOpenIddictServerHandler<HandleRevocationRequestContext>
{
    public static OpenIddictServerHandlerDescriptor Descriptor { get; } =
        OpenIddictServerHandlerDescriptor.CreateBuilder<HandleRevocationRequestContext>()
            .UseScopedHandler<RevokeAuthorizationHandler>()
            .SetOrder(OpenIddictServerHandlers.Revocation.RevokeToken.Descriptor.Order + 1_000)
            .SetType(OpenIddictServerHandlerType.Custom)
            .Build();

    public async ValueTask HandleAsync(HandleRevocationRequestContext context)
    {
        if (context.IsRejected || context.GenericTokenPrincipal is not { } principal
            || !principal.HasTokenType(OpenIddictConstants.TokenTypeIdentifiers.RefreshToken))
        {
            return;
        }

        var authorizationId = principal.GetAuthorizationId();
        if (string.IsNullOrEmpty(authorizationId)
            || await authorizations.FindByIdAsync(authorizationId, context.CancellationToken) is not { } authorization)
        {
            return;
        }

        await authorizations.TryRevokeAsync(authorization, context.CancellationToken);
        await tokens.RevokeByAuthorizationIdAsync(authorizationId, context.CancellationToken);
    }
}
