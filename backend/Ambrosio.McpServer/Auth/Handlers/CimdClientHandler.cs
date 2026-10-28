using Ambrosio.DTO.Settings;
using Ambrosio.McpServer.Auth.Cimd;
using Microsoft.Extensions.Options;
using OpenIddict.Server;
using static OpenIddict.Abstractions.OpenIddictConstants;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace Ambrosio.McpServer.Auth.Handlers;

/// <summary>
/// CIMD (feature 007, T037, R4): quando o <c>client_id</c> é uma URL https, baixa o documento,
/// confere o método de autenticação e a redirect URI e cria (ou atualiza) a aplicação antes de o
/// OpenIddict procurá-la — daí em diante o fluxo é o normal de um cliente público.
/// </summary>
/// <remarks>
/// Roda no <see cref="ProcessAuthenticationContext"/>, que é onde o OpenIddict 7 valida o
/// <c>client_id</c> (<c>ValidateClientId</c>) em todos os endpoints: authorize, token e revoke.
/// </remarks>
public sealed class CimdClientHandler(
    CimdDocumentFetcher fetcher,
    ClientRegistrar registrar,
    IOptions<AuthServerSettings> settings,
    ILogger<CimdClientHandler> logger) : IOpenIddictServerHandler<ProcessAuthenticationContext>
{
    public static OpenIddictServerHandlerDescriptor Descriptor { get; } =
        OpenIddictServerHandlerDescriptor.CreateBuilder<ProcessAuthenticationContext>()
            .UseScopedHandler<CimdClientHandler>()
            .SetOrder(OpenIddictServerHandlers.ValidateClientId.Descriptor.Order - 1_000)
            .SetType(OpenIddictServerHandlerType.Custom)
            .Build();

    public async ValueTask HandleAsync(ProcessAuthenticationContext context)
    {
        var clientId = context.Request?.ClientId;
        if (!CimdDocumentFetcher.IsCimdClientId(clientId))
        {
            return;
        }

        CimdDocument document;
        try
        {
            document = await fetcher.GetAsync(clientId!, context.CancellationToken);
        }
        catch (CimdRejectedException ex)
        {
            logger.LogWarning("Cliente CIMD recusado ({ClientHost}): {Reason}", new Uri(clientId!).Host, ex.Message);
            context.Reject(Errors.InvalidClient, ex.Message);
            return;
        }

        // Só clientes públicos: o AS anuncia `none` e o ChatGPT escolhe pela interseção com a lista
        // dele. Um documento que só aceite outro método não tem como completar o token.
        var methods = (document.TokenEndpointAuthMethodsSupported ?? [])
            .Append(document.TokenEndpointAuthMethod)
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .ToList();

        if (methods.Count > 0 && !methods.Contains(ClientAuthenticationMethods.None, StringComparer.Ordinal))
        {
            context.Reject(Errors.InvalidClient, "O cliente exige um método de autenticação que este servidor não oferece.");
            return;
        }

        var allowed = settings.Value.AllowedRedirectUris;

        // Redirect URIs do documento, filtradas pela lista do servidor. Documento sem a lista: vale a
        // da requisição, se ela for uma das permitidas.
        var redirectUris = (document.RedirectUris ?? [context.Request?.RedirectUri ?? string.Empty])
            .Where(uri => RedirectUriPolicy.IsAllowed(uri, allowed))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var requested = context.Request?.RedirectUri;
        if (requested is not null && !redirectUris.Contains(requested, StringComparer.Ordinal))
        {
            context.Reject(Errors.InvalidClient, "A redirect_uri não consta no documento do cliente.");
            return;
        }

        if (redirectUris.Count == 0)
        {
            context.Reject(Errors.InvalidClient, "O documento do cliente não tem redirect_uri permitida.");
            return;
        }

        await registrar.UpsertCimdAsync(clientId!, document.ClientName, redirectUris, context.CancellationToken);
    }
}
