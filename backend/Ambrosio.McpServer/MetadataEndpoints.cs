using System.Text.Json;
using Ambrosio.DTO.Settings;
using Ambrosio.McpServer.Auth;
using Microsoft.Extensions.Options;

namespace Ambrosio.McpServer;

/// <summary>
/// Documento de autorização do AS (RFC 8414) nos dois caminhos que o ChatGPT consulta
/// (feature 007, T019, contracts/oauth-endpoints.md §1).
/// </summary>
/// <remarks>
/// O OpenIddict já publica o dele em <c>/auth/.well-known/openid-configuration</c>. Este endpoint não
/// é uma cópia por capricho: com o issuer tendo caminho (<c>/auth</c>), a RFC 8414 exige que a
/// descoberta apareça em <c>/.well-known/oauth-authorization-server/{host}/auth</c>, e o OpenIddict
/// não serve esse formato de URL. Acrescentar <c>authorization_details_types_supported</c> — campo
/// que a biblioteca não emite e que é o contrato das tools de pedido (R5) — precisaria de qualquer
/// forma de um documento nosso.
/// <para>
/// Os caminhos saem das <b>mesmas</b> configurações registradas em <see cref="OpenIddictSetup"/>: se
/// algum dos dois mudar sem a outra metade, o teste de contrato (T075) e o fluxo ponta a ponta (T030)
/// quebram juntos, que é o comportamento desejado.
/// </para>
/// </remarks>
public static class MetadataEndpoints
{
    /// <summary>Prefixo RFC 8414 para issuer com caminho.</summary>
    public const string AsMetadataPath = "/.well-known/oauth-authorization-server";

    /// <summary>Único tipo de authorization detail deste feature (R5).</summary>
    public const string AmbrosioDetailType = "ambrosio_order";

    /// <summary>
    /// Mapeia <c>/.well-known/oauth-authorization-server</c> e a variante com caminho do issuer.
    /// Anônimo por definição: o cliente precisa descobrir antes de ter qualquer credencial.
    /// </summary>
    public static IEndpointRouteBuilder MapAmbrosioMetadataEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var document = Build(endpoints.ServiceProvider);

        endpoints.MapMethods(AsMetadataPath, ["GET", "OPTIONS"], () => Results.Json(document))
            .AllowAnonymous()
            .DisableAntiforgery();

        // `/{**path}` captura o <host>/<caminho-do-issuer> que a RFC 8414 acrescenta ao prefixo.
        // Sem restrição de valor o endpoint aceitaria qualquer sufixo; o filtro abaixo mantém o
        // comportamento restrito sem precisar de uma tabela de caminhos.
        endpoints.MapMethods($"{AsMetadataPath}/{{**path}}", ["GET", "OPTIONS"], (string? path) =>
            path is null or "" ? Results.Json(document) : Results.NotFound())
            .AllowAnonymous()
            .DisableAntiforgery();

        // A variante "/auth/mcp" do contrato: mesmo documento, caminho diferente.
        endpoints.Map($"{AsMetadataPath}/auth", () => Results.Json(document))
            .AllowAnonymous()
            .DisableAntiforgery();

        return endpoints;
    }

    /// <summary>Monta o documento a partir da configuração — nada aqui é constante de protocolo.</summary>
    private static object Build(IServiceProvider services)
    {
        var authServer = services.GetRequiredService<IOptions<AuthServerSettings>>().Value;
        var mcp = services.GetRequiredService<IOptions<McpSettings>>().Value;

        var issuer = authServer.Issuer.TrimEnd('/');

        return new
        {
            issuer,

            // Endpoints sob /auth, espelhando OpenIddictSetup.
            authorization_endpoint = $"{issuer}/connect/authorize",
            token_endpoint = $"{issuer}/connect/token",
            userinfo_endpoint = $"{issuer}/connect/userinfo",
            revocation_endpoint = $"{issuer}/connect/revoke",
            registration_endpoint = $"{issuer}/connect/register",
            jwks_uri = $"{issuer}/.well-known/jwks",

            response_types_supported = new[] { "code" },
            grant_types_supported = new[] { "authorization_code", "refresh_token" },
            response_modes_supported = new[] { "query" },

            // PKCE S256 é o único método aceito (T030 confirma que `plain` vira invalid_request).
            code_challenge_methods_supported = new[] { "S256" },

            // Sem segredo de cliente: público + chave assimétrica (R4).
            token_endpoint_auth_methods_supported = new[] { "none", "private_key_jwt" },

            scopes_supported = ScopeSeeder.ScopeNames,

            // FR-025: a lista explícita de escopos que a tela mostra, em português.
            scopes = ScopeSeeder.Scopes.Select(s => new
            {
                name = s.Name,
                display = s.DisplayName,
                description = s.Description
            }),

            claims_supported = new[]
            {
                "sub", "email", "name", "aud", "iss", "exp", "iat", "client_id", "scope"
            },

            // O campo que a OpenAI lê para mandar `authorization_details` (R5). Sem ele, o
            // `ambrosio_order` das tools de pedido não tem como ser anunciado.
            authorization_details_types_supported = new[] { AmbrosioDetailType },

            // Recurso protegido (RFC 9728) e o documento que o descreve.
            resource_metadata = mcp.ResourceUrl,
            authorization_response_iss_parameter_supported = true,

            service_documentation = "https://ambrosiaai.site",
            op_policy_uri = "https://ambrosiaai.site/privacidade",
            op_tos_uri = "https://ambrosiaai.site/termos"
        };
    }
}
