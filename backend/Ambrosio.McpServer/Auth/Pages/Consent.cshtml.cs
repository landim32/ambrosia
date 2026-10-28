using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Ambrosio.McpServer.Auth.Pages;

/// <summary>
/// Consentimento (feature 007, T042): mostra os escopos pedidos com o texto em pt-BR do
/// <see cref="IOpenIddictScopeManager"/> e devolve a decisão ao próprio authorize, que grava a
/// autorização permanente ou responde <c>access_denied</c>.
/// </summary>
public sealed class ConsentModel(
    IOpenIddictApplicationManager applications,
    IOpenIddictScopeManager scopeManager) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    /// <summary>Parâmetros OAuth originais, reenviados como campos ocultos ao authorize.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Parameters { get; private set; } = [];

    public string ClientName { get; private set; } = "ChatGPT";

    public IReadOnlyList<(string Name, string DisplayName, string Description)> Scopes { get; private set; } = [];

    /// <summary>Destino do formulário: o authorize, sob o PathBase.</summary>
    public string AuthorizeAction => $"{Request.PathBase}{Endpoints.AuthorizationEndpoint.Path}";

    public async Task<IActionResult> OnGetAsync()
    {
        if (!LoginModel.IsSafeReturnUrl(ReturnUrl))
        {
            return BadRequest();
        }

        var session = await HttpContext.AuthenticateAsync(McpSetup.AuthSessionScheme);
        if (!session.Succeeded)
        {
            return LocalRedirect($"{Request.PathBase}/login?returnUrl={Uri.EscapeDataString(ReturnUrl!)}");
        }

        var queryIndex = ReturnUrl!.IndexOf('?');
        var query = queryIndex < 0
            ? new Dictionary<string, StringValues>()
            : QueryHelpers.ParseQuery(ReturnUrl[queryIndex..]);

        Parameters = query
            .SelectMany(p => p.Value.Select(v => new KeyValuePair<string, string>(p.Key, v ?? string.Empty)))
            .ToList();

        if (query.TryGetValue(OpenIddictConstants.Parameters.ClientId, out var clientId)
            && await applications.FindByClientIdAsync(clientId.ToString()) is { } application)
        {
            ClientName = await applications.GetDisplayNameAsync(application) ?? ClientName;
        }

        var requested = query.TryGetValue(OpenIddictConstants.Parameters.Scope, out var scope)
            ? scope.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)
            : [];

        var scopes = new List<(string, string, string)>();
        foreach (var name in requested.Distinct(StringComparer.Ordinal))
        {
            if (await scopeManager.FindByNameAsync(name) is { } entry)
            {
                scopes.Add((
                    name,
                    await scopeManager.GetDisplayNameAsync(entry) ?? name,
                    await scopeManager.GetDescriptionAsync(entry) ?? string.Empty));
            }
        }

        Scopes = scopes;

        return Page();
    }
}
