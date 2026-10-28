using Ambrosio.DTO.Settings;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using static OpenIddict.Abstractions.OpenIddictConstants;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace Ambrosio.McpServer.Auth.Handlers;

/// <summary>
/// <c>resource</c> obrigatório e exato (RFC 8707, feature 007, T035, R5).
/// </summary>
/// <remarks>
/// O OpenIddict 7 já recusa recurso não registrado (<c>RegisterResources</c>, configurado com
/// <c>Mcp:ResourceUrl</c>), mas aceita a requisição <b>sem</b> o parâmetro. Este handler fecha essa
/// lacuna: no authorize e na troca do código, <c>resource</c> tem de vir e tem de ser exatamente a URL
/// do MCP (com barra no final é outro recurso). No refresh ele é opcional — o recurso já está gravado
/// na autorização — mas, se vier, também precisa bater.
/// </remarks>
public sealed class ResourceIndicatorHandler(IOptions<McpSettings> settings) :
    IOpenIddictServerHandler<ValidateAuthorizationRequestContext>,
    IOpenIddictServerHandler<ValidateTokenRequestContext>
{
    public const string InvalidTarget = "invalid_target";

    public static OpenIddictServerHandlerDescriptor AuthorizationDescriptor { get; } =
        OpenIddictServerHandlerDescriptor.CreateBuilder<ValidateAuthorizationRequestContext>()
            .UseSingletonHandler<ResourceIndicatorHandler>()
            .SetOrder(OpenIddictServerHandlers.Authentication.ValidateResourceParameter.Descriptor.Order + 1)
            .SetType(OpenIddictServerHandlerType.Custom)
            .Build();

    public static OpenIddictServerHandlerDescriptor TokenDescriptor { get; } =
        OpenIddictServerHandlerDescriptor.CreateBuilder<ValidateTokenRequestContext>()
            .UseSingletonHandler<ResourceIndicatorHandler>()
            .SetOrder(OpenIddictServerHandlers.Exchange.ValidateResourceParameter.Descriptor.Order + 1)
            .SetType(OpenIddictServerHandlerType.Custom)
            .Build();

    public ValueTask HandleAsync(ValidateAuthorizationRequestContext context)
    {
        Validate(context.Request.Resources, required: true, (error, description) => context.Reject(error, description));

        return default;
    }

    public ValueTask HandleAsync(ValidateTokenRequestContext context)
    {
        var required = context.Request.IsAuthorizationCodeGrantType();
        Validate(context.Request.Resources, required, (error, description) => context.Reject(error, description));

        return default;
    }

    private void Validate(System.Collections.Immutable.ImmutableArray<string?>? values, bool required, Action<string, string> reject)
    {
        var expected = settings.Value.ResourceUrl;
        var resources = values is { IsDefault: false } array ? array.ToArray() : null;

        if (resources is null || resources.Length == 0)
        {
            if (required)
            {
                reject(InvalidTarget, $"O parâmetro resource é obrigatório e deve ser {expected}.");
            }

            return;
        }

        if (resources.Length != 1 || !string.Equals(resources[0], expected, StringComparison.Ordinal))
        {
            reject(InvalidTarget, $"O parâmetro resource deve ser exatamente {expected}.");
        }
    }
}
