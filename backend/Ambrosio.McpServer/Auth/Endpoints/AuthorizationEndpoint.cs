using System.Collections.Immutable;
using System.Security.Claims;
using Ambrosio.Domain.Exceptions;
using Ambrosio.Domain.Interfaces;
using Ambrosio.DTO.Settings;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Ambrosio.McpServer.Auth.Endpoints;

/// <summary>
/// Authorize em passthrough (feature 007, T039, contracts/oauth-endpoints.md §3). O OpenIddict já
/// validou cliente, redirect URI, PKCE, escopos e <c>resource</c> antes de chegar aqui; este endpoint
/// decide entre mandar para o login, para o consentimento, ou emitir o código.
/// </summary>
public static class AuthorizationEndpoint
{
    /// <summary>Relativo ao PathBase <c>/auth</c>.</summary>
    public const string Path = "/connect/authorize";

    /// <summary>Campo do formulário de consentimento.</summary>
    public const string DecisionField = "decision";

    public const string DecisionAccept = "accept";

    public const string DecisionDeny = "deny";

    public static IEndpointRouteBuilder MapAmbrosioAuthorizationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // O POST é o formulário de consentimento; o antiforgery é validado à mão abaixo, porque o
        // GET do mesmo endpoint (o ChatGPT abrindo o authorize) não tem token nenhum.
        endpoints.MapMethods(Path, [HttpMethods.Get, HttpMethods.Post], AuthorizeAsync).DisableAntiforgery();
        endpoints.MapPost("/connect/token", TokenAsync).DisableAntiforgery();
        endpoints.MapMethods("/connect/userinfo", [HttpMethods.Get, HttpMethods.Post], UserInfoAsync).DisableAntiforgery();

        return endpoints;
    }

    private static async Task<IResult> AuthorizeAsync(
        HttpContext context,
        IOpenIddictApplicationManager applications,
        IOpenIddictAuthorizationManager authorizations,
        IUserService users,
        IAntiforgery antiforgery,
        IOptions<McpSettings> mcp)
    {
        var request = context.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("Requisição de autorização do OpenIddict ausente.");

        string? decision = null;
        if (HttpMethods.IsPost(context.Request.Method) && context.Request.HasFormContentType)
        {
            decision = context.Request.Form[DecisionField].ToString();
            if (!string.IsNullOrEmpty(decision))
            {
                await antiforgery.ValidateRequestAsync(context);
            }
        }

        var returnUrl = BuildReturnUrl(context, request);

        // Sem sessão no AS (ou pedido explícito de login): vai para a tela de login e volta para cá.
        var session = await context.AuthenticateAsync(McpSetup.AuthSessionScheme);
        if (!session.Succeeded
            || !long.TryParse(session.Principal?.FindFirst(Claims.Subject)?.Value, out var userId)
            || request.HasPromptValue(PromptValues.Login))
        {
            if (request.HasPromptValue(PromptValues.None))
            {
                return Forbid(Errors.LoginRequired, "É preciso entrar no Ambrosio.");
            }

            return Results.Redirect($"{context.Request.PathBase}/login?returnUrl={Uri.EscapeDataString(returnUrl)}");
        }

        Ambrosio.DTO.Users.UserInfo user;
        try
        {
            user = await users.GetByIdAsync(userId);
        }
        catch (NotFoundException)
        {
            // Conta apagada depois do login: a sessão não vale mais.
            await context.SignOutAsync(McpSetup.AuthSessionScheme);

            return Results.Redirect($"{context.Request.PathBase}/login?returnUrl={Uri.EscapeDataString(returnUrl)}");
        }

        var application = await applications.FindByClientIdAsync(request.ClientId!)
            ?? throw new InvalidOperationException("Cliente validado pelo OpenIddict não foi encontrado.");
        var applicationId = await applications.GetIdAsync(application);
        var subject = userId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var scopes = request.GetScopes();

        if (decision == DecisionDeny)
        {
            return Forbid(Errors.AccessDenied, "A vendedora negou o acesso.");
        }

        // Consentimento permanente que já cobre todos os escopos pedidos: emite o código direto.
        var existing = new List<object>();
        await foreach (var authorization in authorizations.FindAsync(
                           subject: subject,
                           client: applicationId,
                           status: Statuses.Valid,
                           type: AuthorizationTypes.Permanent,
                           scopes: scopes))
        {
            existing.Add(authorization);
        }

        if (existing.Count == 0 && decision != DecisionAccept)
        {
            if (request.HasPromptValue(PromptValues.None))
            {
                return Forbid(Errors.ConsentRequired, "É preciso autorizar o acesso.");
            }

            return Results.Redirect($"{context.Request.PathBase}/consent?returnUrl={Uri.EscapeDataString(returnUrl)}");
        }

        var identity = new ClaimsIdentity(
            authenticationType: TokenValidationParameters.DefaultAuthenticationType,
            nameType: Claims.Name,
            roleType: Claims.Role);

        identity.SetClaim(Claims.Subject, subject)
            .SetClaim(Claims.Email, user.Email)
            .SetClaim(Claims.Name, user.Name);

        identity.SetScopes(scopes);

        // `aud` do access token = URL do MCP (R5), o valor exato que o JwtBearer do /mcp exige.
        identity.SetResources(mcp.Value.ResourceUrl);

        // Ampliação (T062): a nova autorização permanente soma os escopos já concedidos a este cliente
        // aos pedidos agora, para o próximo authorize com qualquer combinação deles não pedir
        // consentimento de novo. O token continua só com os escopos desta requisição.
        var permanent = existing.LastOrDefault()
            ?? await authorizations.CreateAsync(
                identity: identity,
                subject: subject,
                client: applicationId!,
                type: AuthorizationTypes.Permanent,
                scopes: await MergeGrantedScopesAsync(authorizations, subject, applicationId!, identity.GetScopes()));

        identity.SetAuthorizationId(await authorizations.GetIdAsync(permanent));
        identity.SetDestinations(GetDestinations);

        return Results.SignIn(new ClaimsPrincipal(identity), properties: null,
            authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    /// <summary>
    /// Troca de código e refresh (contracts §4). O principal vem do código ou do refresh já validado
    /// pelo OpenIddict; aqui só se confere que a conta ainda existe e se atualizam nome e e-mail.
    /// </summary>
    private static async Task<IResult> TokenAsync(HttpContext context, IUserService users)
    {
        var request = context.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("Requisição de token do OpenIddict ausente.");

        if (!request.IsAuthorizationCodeGrantType() && !request.IsRefreshTokenGrantType())
        {
            return Forbid(Errors.UnsupportedGrantType, "Grant não suportado.");
        }

        var result = await context.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        var principal = result.Principal
            ?? throw new InvalidOperationException("Principal do código/refresh ausente.");

        if (!long.TryParse(principal.GetClaim(Claims.Subject), out var userId))
        {
            return Forbid(Errors.InvalidGrant, "O token não identifica uma conta.");
        }

        Ambrosio.DTO.Users.UserInfo user;
        try
        {
            user = await users.GetByIdAsync(userId);
        }
        catch (NotFoundException)
        {
            return Forbid(Errors.InvalidGrant, "A conta do Ambrosio não existe mais.");
        }

        var identity = new ClaimsIdentity(
            principal.Claims,
            authenticationType: TokenValidationParameters.DefaultAuthenticationType,
            nameType: Claims.Name,
            roleType: Claims.Role);

        identity.SetClaim(Claims.Email, user.Email)
            .SetClaim(Claims.Name, user.Name);

        identity.SetDestinations(GetDestinations);

        return Results.SignIn(new ClaimsPrincipal(identity), properties: null,
            authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    /// <summary>Userinfo (contracts §5, T044): <c>sub</c>, e <c>email</c>/<c>name</c> conforme os escopos.</summary>
    private static async Task<IResult> UserInfoAsync(HttpContext context, IUserService users)
    {
        var result = await context.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        var principal = result.Principal;

        if (principal is null || !long.TryParse(principal.GetClaim(Claims.Subject), out var userId))
        {
            return Results.Challenge(
                new AuthenticationProperties(new Dictionary<string, string?>
                {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidToken,
                    [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "Token inválido."
                }),
                [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
        }

        Ambrosio.DTO.Users.UserInfo user;
        try
        {
            user = await users.GetByIdAsync(userId);
        }
        catch (NotFoundException)
        {
            return Results.Challenge(
                new AuthenticationProperties(new Dictionary<string, string?>
                {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidToken,
                    [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "A conta não existe mais."
                }),
                [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
        }

        var claims = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            [Claims.Subject] = userId.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };

        if (principal.HasScope(Scopes.Email))
        {
            claims[Claims.Email] = user.Email;

            // E-mail vem do cadastro (validado pelo Google ou digitado no registro com senha); a
            // OpenAI exige `email_verified: true` para restrição de domínio de workspace.
            claims[Claims.EmailVerified] = true;
        }

        if (principal.HasScope(Scopes.Profile))
        {
            claims[Claims.Name] = user.Name;
        }

        return Results.Ok(claims);
    }

    /// <summary>
    /// Access token só com <c>sub</c> (mais <c>scope</c> e <c>client_id</c>, que o OpenIddict põe);
    /// nome e e-mail vão só no id token, e só com os escopos correspondentes.
    /// </summary>
    private static IEnumerable<string> GetDestinations(Claim claim)
    {
        switch (claim.Type)
        {
            case Claims.Subject:
                yield return Destinations.AccessToken;
                yield return Destinations.IdentityToken;
                yield break;

            case Claims.Name when claim.Subject?.HasScope(Scopes.Profile) == true:
                yield return Destinations.IdentityToken;
                yield break;

            case Claims.Email when claim.Subject?.HasScope(Scopes.Email) == true:
                yield return Destinations.IdentityToken;
                yield break;

            default:
                yield break;
        }
    }

    private static async Task<ImmutableArray<string>> MergeGrantedScopesAsync(
        IOpenIddictAuthorizationManager authorizations,
        string subject,
        string applicationId,
        ImmutableArray<string> requested)
    {
        var merged = new SortedSet<string>(requested, StringComparer.Ordinal);

        await foreach (var authorization in authorizations.FindAsync(
                           subject: subject,
                           client: applicationId,
                           status: Statuses.Valid,
                           type: AuthorizationTypes.Permanent,
                           scopes: null))
        {
            merged.UnionWith(await authorizations.GetScopesAsync(authorization));
        }

        return [.. merged];
    }

    /// <summary>
    /// URL do authorize com os parâmetros OAuth originais, para voltar depois do login ou do
    /// consentimento. Campos do formulário (decisão e antiforgery) ficam de fora.
    /// </summary>
    private static string BuildReturnUrl(HttpContext context, OpenIddictRequest request)
    {
        var parameters = request.GetParameters()
            .Where(p => p.Key is not DecisionField and not McpSetup.AntiforgeryFieldName)
            .SelectMany(p => ((ImmutableArray<string?>?)p.Value ?? []).Select(v => new KeyValuePair<string, string?>(p.Key, v)));

        return context.Request.PathBase + Path + QueryString.Create(parameters).ToUriComponent();
    }

    private static IResult Forbid(string error, string description) =>
        Results.Forbid(
            new AuthenticationProperties(new Dictionary<string, string?>
            {
                [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
                [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description
            }),
            [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
}
