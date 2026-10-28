namespace Ambrosio.McpServer.Mcp;

/// <summary>
/// Monta o desafio <c>WWW-Authenticate</c> do recurso protegido (RFC 6750 + RFC 9728), usado tanto
/// no 401 HTTP de token inválido quanto no <c>_meta["mcp/www_authenticate"]</c> de uma tool sem token
/// ou sem escopo (contracts/mcp-tools.md, "Resultado de desafio").
/// </summary>
public static class McpChallenge
{
    public const string InvalidToken = "invalid_token";

    public const string InsufficientScope = "insufficient_scope";

    /// <summary>
    /// Endereço do Protected Resource Metadata do recurso: a RFC 9728 insere
    /// <c>/.well-known/oauth-protected-resource</c> entre a origem e o caminho do recurso.
    /// </summary>
    public static string ResourceMetadataUrl(string resourceUrl)
    {
        var uri = new Uri(resourceUrl, UriKind.Absolute);
        var path = uri.AbsolutePath == "/" ? string.Empty : uri.AbsolutePath;

        return $"{uri.GetLeftPart(UriPartial.Authority)}/.well-known/oauth-protected-resource{path}";
    }

    /// <summary>
    /// Desafio de requisição sem credencial (RFC 6750 §3: sem <c>error</c>), com os escopos que o
    /// cliente deve pedir no authorize.
    /// </summary>
    public static string BuildUnauthenticated(string resourceUrl, IEnumerable<string> scopes) =>
        $"Bearer resource_metadata=\"{ResourceMetadataUrl(resourceUrl)}\", scope=\"{string.Join(' ', scopes)}\"";

    public static string Build(string resourceUrl, string error, string description, IEnumerable<string>? scopes = null)
    {
        var header =
            $"Bearer resource_metadata=\"{ResourceMetadataUrl(resourceUrl)}\", " +
            $"error=\"{error}\", error_description=\"{Escape(description)}\"";

        var scopeList = scopes?.ToList();
        if (scopeList is { Count: > 0 })
        {
            header += $", scope=\"{string.Join(' ', scopeList)}\"";
        }

        return header;
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
