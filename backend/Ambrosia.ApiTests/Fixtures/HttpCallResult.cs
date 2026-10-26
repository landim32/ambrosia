using Flurl.Http;

namespace Ambrosia.ApiTests.Fixtures;

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

    public bool IsSuccess => StatusCode is >= 200 and < 300;

    internal static async Task<HttpCallResult> From(IFlurlResponse response)
    {
        var body = await response.GetStringAsync();

        // Flurl 4 usa sua própria coleção de cabeçalhos (Flurl.Util.IReadOnlyNameValueList):
        // TryGetFirst compara nomes sem diferenciar maiúsculas, como exige o HTTP.
        response.Headers.TryGetFirst("Location", out var location);

        return new HttpCallResult
        {
            StatusCode = response.StatusCode,
            Body = body,
            Location = location
        };
    }

    /// <summary>Corpo desserializado com as opções do contrato (camelCase, enums como string).</summary>
    public T As<T>() => ApiTestFixture.Deserialize<T>(Body);

    public override string ToString() => $"HTTP {StatusCode} {Body}";
}

/// <summary>Leitura de status HTTP nos testes (evita mágica numérica espalhada).</summary>
public static class HttpTestExtensions
{
    public static bool IsSuccess(this int statusCode) => statusCode is >= 200 and < 300;
}
