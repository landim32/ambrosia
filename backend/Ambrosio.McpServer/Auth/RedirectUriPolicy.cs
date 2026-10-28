namespace Ambrosio.McpServer.Auth;

/// <summary>
/// Regra única de redirect URI aceita pelo authorization server (DCR e CIMD, R4), lida de
/// <c>Auth:AllowedRedirectUris</c>.
/// </summary>
/// <remarks>
/// Uma entrada terminada em <c>/{callback_id}</c> casa com qualquer valor naquele último segmento —
/// o formato da redirect URI por conexão do ChatGPT — mas só com um segmento simples: sem barra,
/// query ou fragmento, para que <c>…/oauth/x/../../outra</c> ou <c>…/oauth/x?next=</c> não passem.
/// </remarks>
public static class RedirectUriPolicy
{
    private const string CallbackPlaceholder = "{callback_id}";

    public static bool IsAllowed(string? redirectUri, IEnumerable<string> allowed)
    {
        if (string.IsNullOrWhiteSpace(redirectUri)
            || !Uri.TryCreate(redirectUri, UriKind.Absolute, out var uri)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        foreach (var entry in allowed)
        {
            if (string.Equals(entry, redirectUri, StringComparison.Ordinal))
            {
                return true;
            }

            if (!entry.EndsWith(CallbackPlaceholder, StringComparison.Ordinal))
            {
                continue;
            }

            var prefix = entry[..^CallbackPlaceholder.Length];
            if (!redirectUri.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var callbackId = redirectUri[prefix.Length..];
            if (callbackId.Length > 0 && callbackId.Length <= 200
                && callbackId.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or '~')
                && callbackId is not "." and not "..")
            {
                return true;
            }
        }

        return false;
    }
}
