using OpenIddict.Server;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace Ambrosio.McpServer.Auth.Handlers;

/// <summary>
/// Registra em Warning toda resposta de erro do authorization server (authorize, token, userinfo e
/// revoke), com o endpoint, o <c>client_id</c>, o erro e a descrição — nunca o código, os tokens ou a
/// asserção do cliente. Produção só registra Warning para cima, e o OpenIddict loga as recusas em
/// Information: sem isto, uma conexão do ChatGPT que falha no OAuth não deixa rastro nenhum.
/// </summary>
public sealed class OAuthRejectionLogger(ILogger<OAuthRejectionLogger> logger) :
    IOpenIddictServerHandler<ApplyAuthorizationResponseContext>,
    IOpenIddictServerHandler<ApplyTokenResponseContext>,
    IOpenIddictServerHandler<ApplyUserInfoResponseContext>,
    IOpenIddictServerHandler<ApplyRevocationResponseContext>
{
    /// <summary>Primeiro handler de cada resposta: registra antes de o OpenIddict aplicá-la.</summary>
    private const int Order = int.MinValue + 100_000;

    public static IEnumerable<OpenIddictServerHandlerDescriptor> Descriptors { get; } =
    [
        OpenIddictServerHandlerDescriptor.CreateBuilder<ApplyAuthorizationResponseContext>()
            .UseSingletonHandler<OAuthRejectionLogger>().SetOrder(Order).SetType(OpenIddictServerHandlerType.Custom).Build(),
        OpenIddictServerHandlerDescriptor.CreateBuilder<ApplyTokenResponseContext>()
            .UseSingletonHandler<OAuthRejectionLogger>().SetOrder(Order).SetType(OpenIddictServerHandlerType.Custom).Build(),
        OpenIddictServerHandlerDescriptor.CreateBuilder<ApplyUserInfoResponseContext>()
            .UseSingletonHandler<OAuthRejectionLogger>().SetOrder(Order).SetType(OpenIddictServerHandlerType.Custom).Build(),
        OpenIddictServerHandlerDescriptor.CreateBuilder<ApplyRevocationResponseContext>()
            .UseSingletonHandler<OAuthRejectionLogger>().SetOrder(Order).SetType(OpenIddictServerHandlerType.Custom).Build()
    ];

    public ValueTask HandleAsync(ApplyAuthorizationResponseContext context) =>
        Log("authorize", context.Request?.ClientId, context.Response?.Error, context.Response?.ErrorDescription);

    public ValueTask HandleAsync(ApplyTokenResponseContext context) =>
        Log($"token ({context.Request?.GrantType})", context.Request?.ClientId, context.Response?.Error, context.Response?.ErrorDescription);

    public ValueTask HandleAsync(ApplyUserInfoResponseContext context) =>
        Log("userinfo", context.Request?.ClientId, context.Response?.Error, context.Response?.ErrorDescription);

    public ValueTask HandleAsync(ApplyRevocationResponseContext context) =>
        Log("revoke", context.Request?.ClientId, context.Response?.Error, context.Response?.ErrorDescription);

    private ValueTask Log(string endpoint, string? clientId, string? error, string? description)
    {
        if (!string.IsNullOrEmpty(error))
        {
            logger.LogWarning(
                "OAuth {Endpoint} recusado para o cliente {ClientId}: {Error} — {Description}",
                endpoint, clientId ?? "(sem client_id)", error, description);
        }

        return default;
    }
}
