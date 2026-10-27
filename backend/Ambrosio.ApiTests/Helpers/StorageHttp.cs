using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Ambrosio.ApiTests.Helpers;

/// <summary>
/// Fala <b>direto com o storage</b>, sem passar pela API (skill <c>dotnet-test-api</c>, research §16):
/// os cenários que a suíte precisa provar são comportamento do próprio provedor — o <c>PUT</c> na URL
/// pré-assinada, o <c>GET</c> sem assinatura, o 404 do objeto apagado e o preflight CORS. Nada disso
/// é observável pelos endpoints.
/// <para>
/// Só usa as URLs que a API devolveu. Os testes <b>nunca</b> recebem credencial do storage: quem não
/// existe é o caso do 403 "sem assinatura", e ele se obtém cortando a query string da URL de leitura.
/// </para>
/// </summary>
public static class StorageHttp
{
    /// <summary>Cliente compartilhado: os testes de storage abrem muitas conexões curtas ao mesmo host.</summary>
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromMinutes(2)
    };

    /// <summary>
    /// <c>PUT</c> do binário na URL pré-assinada com o <c>Content-Type</c> pedido — que é justamente o
    /// cabeçalho assinado, então trocá-lo é o que produz o 403 do storage (US1 cenário 2).
    /// </summary>
    public static async Task<HttpResponseMessage> PutAsync(string url, byte[] content, string contentType)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, url)
        {
            Content = new ByteArrayContent(content)
        };

        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);

        return await Http.SendAsync(request);
    }

    /// <summary><c>GET</c> simples (URL assinada, ou a mesma sem assinatura para provar o 403).</summary>
    public static async Task<HttpResponseMessage> GetAsync(string url) => await Http.GetAsync(url);

    /// <summary>
    /// Preflight CORS: <c>OPTIONS</c> com <c>Origin</c> e <c>Access-Control-Request-Method</c>, que é o
    /// que o navegador manda antes do <c>PUT</c> (FR-022).
    /// </summary>
    public static async Task<HttpResponseMessage> OptionsAsync(string url, string origin, string method)
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, url);

        request.Headers.TryAddWithoutValidation("Origin", origin);
        request.Headers.TryAddWithoutValidation("Access-Control-Request-Method", method);
        request.Headers.TryAddWithoutValidation("Access-Control-Request-Headers", "content-type");

        return await Http.SendAsync(request);
    }

    /// <summary>Lê o corpo como bytes (para comparar byte a byte com o arquivo enviado).</summary>
    public static async Task<byte[]> ReadBytesAsync(HttpResponseMessage response) =>
        await response.Content.ReadAsByteArrayAsync();

    /// <summary>
    /// Endereço do objeto sem a query string assinada: o bucket é privado, então chamar este endereço
    /// responde 403 (US2 cenário 2, FR-004).
    /// </summary>
    public static string StripQuery(string url)
    {
        var query = url.IndexOf('?');

        return query < 0 ? url : url[..query];
    }

    /// <summary>SHA-256 em hex minúsculo — o mesmo formato que a API declara e a coluna <c>sha256</c> guarda.</summary>
    public static string Sha256Hex(byte[] data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    /// <summary>SHA-256 do arquivo do fixture, lido da pasta de saída dos testes.</summary>
    public static async Task<string> Sha256HexOfFileAsync(string path) =>
        Sha256Hex(await File.ReadAllBytesAsync(path));

    /// <summary>
    /// Caminho absoluto de um fixture em <c>Fixtures/Images</c> a partir da pasta de saída da execução.
    /// </summary>
    public static string FixturePath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Images", fileName);

    /// <summary>Texto legível do corpo de uma resposta do storage (para a mensagem de falha do teste).</summary>
    public static async Task<string> DescribeAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();

        var builder = new StringBuilder($"HTTP {(int)response.StatusCode} {response.StatusCode}");

        if (!string.IsNullOrWhiteSpace(body))
        {
            builder.Append(" | ").Append(body.Length > 400 ? body[..400] : body);
        }

        return builder.ToString();
    }

    public static bool IsForbidden(HttpStatusCode status) => status == HttpStatusCode.Forbidden;
}
