using Ambrosio.DTO.Settings;
using Ambrosio.Infra.Context;
using Ambrosio.Infra.Context.OAuth;
using Ambrosio.McpServer.Auth;

namespace Ambrosio.McpServer;

/// <summary>
/// Configuração do authorization server OAuth 2.1 (feature 007, R3/R5/R13/R14).
/// </summary>
/// <remarks>
/// O que o ChatGPT exige e o OpenIddict não traz pronto — CIMD, DCR, <c>resource</c> (RFC 8707) e
/// <c>iss</c> na resposta (RFC 9207) — vira handler em tasks próprios (R4/R5). Aqui fica o núcleo do
/// protocolo: issuer explícito, os endpoints sob <c>/auth</c>, PKCE S256 obrigatório, access token
/// JWT não criptografado e a chave RSA do <see cref="SigningKeyProvider"/>.
/// </remarks>
public static class OpenIddictSetup
{
    /// <summary>Caminho do AS atrás do gateway; também o PathBase do host (R8/T015).</summary>
    public const string AuthPathBase = "/auth";

    /// <summary>
    /// Registra o OpenIddict sobre o <see cref="AmbrosioContext"/> existente, com as entidades de
    /// chave <see cref="long"/> mapeadas em <c>OAuthSetup</c> (R15).
    /// </summary>
    public static IServiceCollection AddAmbrosioAuthorizationServer(
        this IServiceCollection services,
        IConfiguration configuration,
        SigningKeyProvider signingKey)
    {
        var authServer = configuration.GetSection("Auth").Get<AuthServerSettings>() ?? new AuthServerSettings();

        services.Configure<AuthServerSettings>(configuration.GetSection("Auth"));

        // Instância já resolvida pelo Program: o host precisa do `kid` e da origem em log antes de
        // registrar serviço algum, e um container construído aqui produziria duas chaves locais
        // diferentes em Development.
        services.AddSingleton(signingKey);

        services.AddOpenIddict()

            // O store EF fica no AddCore(), não no AddServer() — UseEntityFrameworkCore() é
            // extensão de OpenIddictCoreBuilder.
            .AddCore(core =>
            {
                core.UseEntityFrameworkCore()
                    .UseDbContext<AmbrosioContext>()
                    .ReplaceDefaultEntities<OAuthApplication, OAuthAuthorization, OAuthScope, OAuthToken, long>();
            })

            .AddServer(server =>
            {
                // Issuer explícito: deduzido da requisição ele sairia com o host errado (ou http)
                // atrás do gateway, e o `iss` do token não bateria com o ValidIssuer do MCP (T018).
                server.SetIssuer(new Uri(authServer.Issuer, UriKind.Absolute));

                // Endpoints do contrato (contracts/oauth-endpoints.md §1), todos relativos ao
                // PathBase /auth.
                server.SetAuthorizationEndpointUris("connect/authorize")
                    .SetTokenEndpointUris("connect/token")
                    .SetUserInfoEndpointUris("connect/userinfo")
                    .SetRevocationEndpointUris("connect/revoke")
                    .SetConfigurationEndpointUris(".well-known/openid-configuration")
                    .SetJsonWebKeySetEndpointUris(".well-known/jwks");

                server.AllowAuthorizationCodeFlow()
                    .AllowRefreshTokenFlow();

                // PKCE obrigatório, só S256 (requisito do ChatGPT; `plain` precisa virar
                // invalid_request — T030).
                server.RequireProofKeyForCodeExchange();

                // JWT assinado, não criptografado: o MCP valida com a chave pública do JWKS, então
                // um JWE quebraria a validação cross-host (R13).
                server.DisableAccessTokenEncryption();

                // Validades da spec (R14): acesso 30 min, refresh 14 dias. A rotação com
                // deslizamento e a revogação da família no reuso são o comportamento padrão do
                // OpenIddict para refresh tokens, não uma opção ligada aqui.
                server.SetAccessTokenLifetime(TimeSpan.FromMinutes(authServer.AccessTokenMinutes));
                server.SetRefreshTokenLifetime(TimeSpan.FromDays(authServer.RefreshTokenDays));

                // Passthrough: authorize, token e userinfo caem em controllers/pages deste host,
                // porque é aí que entram o cookie de sessão do AS, o consentimento e o `sub`.
                server.UseAspNetCore()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableTokenEndpointPassthrough()
                    .EnableUserInfoEndpointPassthrough();

                // Assinador único: RSA 2048 (R13). A chave é a do SigningKeyProvider resolvido pelo
                // Program — obrigatoriamente de Auth__SigningKeyPem em Homolog e Production.
                server.AddSigningKey(signingKey.SecurityKey);

                // OpenIddict EXIGE uma chave de criptografia além da de assinatura, e reclama no
                // primeiro request se ela faltar — inclusive com DisableAccessTokenEncryption()
                // ligado, porque a chave também protege os bilhetes internos de autenticação.
                // AddEphemeralEncryptionKey() resolvia o erro e era a armadilha: trocaria a chave a
                // cada restart, exatamente o que R13 proíbe.
                //
                // Reaproveitamos a MESMA RSA da assinatura. É um concessionamento deliberado (uma
                // chave em dois papéis), pago de propósito para não exigir um segundo segredo no
                // ambiente nem um segundo arquivo em %APPDATA%: o requisito real da feature é que a
                // chave sobreviva ao restart, e dividir assinatura de criptografia só seria
                // obrigatório se os dois serviços corressem em hosts separados — o que R8 descartou.
                server.AddEncryptionKey(signingKey.SecurityKey);
            });

        // Sem AddValidation() aqui: proteger `/auth/connect/userinfo` com o pipeline de validação do
        // OpenIddict exigiria o pacote OpenIddict.Validation, e o mesmo access token já é validado
        // pelo JwtBearer contra o JWKS local (T018) — um esquema só, a mesma chave pública para o MCP
        // e para o userinfo, e nada de segunda pilha de validação para manter alinhada.

        return services;
    }
}
