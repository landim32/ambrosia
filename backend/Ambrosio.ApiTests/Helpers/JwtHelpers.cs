namespace Ambrosio.ApiTests.Helpers;

/// <summary>Manipulação de JWT para os testes de SC-004 (acesso não autorizado).</summary>
internal static class JwtHelpers
{
    private const string BASE64URL = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";

    /// <summary>
    /// Troca o primeiro caractere da assinatura, mantendo o formato base64url. O token resultante
    /// continua bem formado (3 partes, mesmo tamanho), então a falha vem da validação criptográfica
    /// e não do parsing — que é exatamente o que SC-004 precisa provar.
    /// </summary>
    public static string TamperSignature(string token)
    {
        var parts = token.Split('.');
        if (parts.Length != 3)
        {
            throw new InvalidOperationException($"Token JWT inesperado (partes={parts.Length}).");
        }

        var signature = parts[2];
        if (signature.Length == 0)
        {
            throw new InvalidOperationException("Token sem assinatura.");
        }

        var first = BASE64URL.IndexOf(signature[0]);
        var replacement = BASE64URL[(first + 1) % BASE64URL.Length];

        parts[2] = replacement + signature[1..];
        return string.Join('.', parts);
    }

    /// <summary>Parte de payload de um JWT como texto decodificado (para inspecionar claims).</summary>
    public static string DecodePayload(string token)
    {
        var payload = token.Split('.')[1];
        var padded = payload.Replace('-', '+').Replace('_', '/');

        // Base64url dispensa o preenchimento: resto 2 pede "==", resto 3 pede "=", resto 0/1 none.
        var missing = padded.Length % 4 switch
        {
            2 => 2,
            3 => 1,
            _ => 0
        };

        padded += new string('=', missing);

        return System.Text.Encoding.UTF8.GetString(System.Convert.FromBase64String(padded));
    }
}
