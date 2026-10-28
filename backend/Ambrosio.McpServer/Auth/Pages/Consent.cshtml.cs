using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Ambrosio.McpServer.Auth.Pages;

/// <summary>Escopo pedido, com o texto em pt-BR e se a vendedora já o concedeu a este cliente.</summary>
public sealed record ConsentScope(string Name, string DisplayName, string Description, bool AlreadyGranted);

/// <summary>Grupo da tela: uma área do Ambrosio (Clientes, Pedidos...).</summary>
public sealed record ConsentArea(string Title, IReadOnlyList<ConsentScope> Scopes);

/// <summary>
/// Consentimento (feature 007, T042/T061): mostra os escopos pedidos agrupados por área, com o texto
/// em pt-BR do <see cref="IOpenIddictScopeManager"/>, marca os que já foram concedidos (ampliação) e
/// devolve a decisão ao próprio authorize, que grava a autorização permanente ou responde
/// <c>access_denied</c>.
/// </summary>
public sealed class ConsentModel(
    IOpenIddictApplicationManager applications,
    IOpenIddictAuthorizationManager authorizations,
    IOpenIddictScopeManager scopeManager) : PageModel
{
    /// <summary>Área de cada prefixo de escopo, na ordem em que aparecem na tela.</summary>
    private static readonly (string Title, Func<string, bool> Matches)[] Areas =
    [
        ("Identidade", s => s is Scopes.OpenId or Scopes.Email or Scopes.Profile or Scopes.OfflineAccess),
        ("Clientes", s => s.StartsWith("customers:", StringComparison.Ordinal)),
        ("Pedidos", s => s.StartsWith("orders:", StringComparison.Ordinal)),
        ("Imagens", s => s.StartsWith("images:", StringComparison.Ordinal)),
        ("Importações do +Vendas", s => s.StartsWith("imports:", StringComparison.Ordinal))
    ];

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    /// <summary>Parâmetros OAuth originais, reenviados como campos ocultos ao authorize.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Parameters { get; private set; } = [];

    public string ClientName { get; private set; } = "ChatGPT";

    public IReadOnlyList<ConsentArea> AreaGroups { get; private set; } = [];

    /// <summary>Já existe consentimento deste cliente: a tela pede só a ampliação.</summary>
    public bool IsExpansion { get; private set; }

    /// <summary>Destino do formulário: o authorize, sob o PathBase.</summary>
    public string AuthorizeAction => $"{Request.PathBase}{Endpoints.AuthorizationEndpoint.Path}";

    public async Task<IActionResult> OnGetAsync()
    {
        if (!LoginModel.IsSafeReturnUrl(ReturnUrl))
        {
            return BadRequest();
        }

        var session = await HttpContext.AuthenticateAsync(McpSetup.AuthSessionScheme);
        var subject = session.Principal?.FindFirst(Claims.Subject)?.Value;
        if (!session.Succeeded || string.IsNullOrEmpty(subject))
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

        var granted = new HashSet<string>(StringComparer.Ordinal);
        if (query.TryGetValue(OpenIddictConstants.Parameters.ClientId, out var clientId)
            && await applications.FindByClientIdAsync(clientId.ToString()) is { } application)
        {
            ClientName = await applications.GetDisplayNameAsync(application) ?? ClientName;
            granted = await GrantedScopesAsync(subject, await applications.GetIdAsync(application));
        }

        IsExpansion = granted.Count > 0;

        var requested = query.TryGetValue(OpenIddictConstants.Parameters.Scope, out var scope)
            ? scope.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal)
            : [];

        var scopes = new List<ConsentScope>();
        foreach (var name in requested)
        {
            if (await scopeManager.FindByNameAsync(name) is { } entry)
            {
                scopes.Add(new ConsentScope(
                    name,
                    await scopeManager.GetDisplayNameAsync(entry) ?? name,
                    await scopeManager.GetDescriptionAsync(entry) ?? string.Empty,
                    granted.Contains(name)));
            }
        }

        AreaGroups = Areas
            .Select(area => new ConsentArea(area.Title, scopes.Where(s => area.Matches(s.Name)).ToList()))
            .Where(area => area.Scopes.Count > 0)
            .ToList();

        return Page();
    }

    /// <summary>Escopos das autorizações permanentes válidas desta vendedora para este cliente.</summary>
    private async Task<HashSet<string>> GrantedScopesAsync(string subject, string? applicationId)
    {
        var granted = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(applicationId))
        {
            return granted;
        }

        await foreach (var authorization in authorizations.FindAsync(
                           subject: subject,
                           client: applicationId,
                           status: Statuses.Valid,
                           type: AuthorizationTypes.Permanent,
                           scopes: null))
        {
            granted.UnionWith(await authorizations.GetScopesAsync(authorization));
        }

        return granted;
    }
}
