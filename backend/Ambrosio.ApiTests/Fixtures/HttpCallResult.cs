using Flurl.Http;

namespace Ambrosio.ApiTests.Fixtures;

/// <summary>
/// Resposta HTTP capturada sem exceção: status, corpo bruto e cabeçalho Location.
/// O corpo bruto é necessário para provar SC-004/SC-005 diretamente no JSON — por exemplo
/// "nenhuma chave contendo 'password' aparece na resposta" — o que a desserialização escondiria.
/// </summary>
public sealed class HttpCallResult
{
    public int StatusCode { get; init; }

    public string Body { get; init; } = string.Empty;

    public string? Location { get; init; }

    /// <summary>
    /// Cabeçalhos da resposta, para as asserções que olham o cabeçalho e não o corpo. Hoje é o
    /// <c>Retry-After</c> do bloqueio de importação (FR-008a): o teste precisa provar que ele traz
    /// <b>segundos</b>, e não o horário absoluto — o que não dá para saber lendo o corpo.
    /// </summary>
    public IReadOnlyDictionary<string, string?> Headers { get; init; } =
        new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

    public bool IsSuccess => StatusCode is >= 200 and < 300;

    internal static async Task<HttpCallResult> From(IFlurlResponse response)
    {
        var body = await response.GetStringAsync();

        // Flurl 4 usa sua própria coleção de cabeçalhos (Flurl.Util.IReadOnlyNameValueList):
        // TryGetFirst compara nomes sem diferenciar maiúsculas, como exige o HTTP.
        response.Headers.TryGetFirst("Location", out var location);

        var headers = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in new[] { "Retry-After", "Location", "Content-Type" })
        {
            response.Headers.TryGetFirst(name, out var value);
            headers[name] = value;
        }

        return new HttpCallResult
        {
            StatusCode = response.StatusCode,
            Body = body,
            Location = location,
            Headers = headers
        };
    }

    /// <summary>Valor de um cabeçalho; <c>null</c> quando ausente.</summary>
    public string? Header(string name) =>
        Headers.TryGetValue(name, out var value) ? value : null;

    /// <summary>Corpo desserializado com as opções do contrato (camelCase, enums como string).</summary>
    public T As<T>() => ApiTestFixture.Deserialize<T>(Body);

    public override string ToString() => $"HTTP {StatusCode} {Body}";
}

/// <summary>Leitura de status HTTP nos testes (evita mágica numérica espalhada).</summary>
public static class HttpTestExtensions
{
    public static bool IsSuccess(this int statusCode) => statusCode is >= 200 and < 300;
}
