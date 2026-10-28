using System.Text.Json;
using Ambrosio.ApiTests.Fixtures;
using Flurl;
using Flurl.Http;

namespace Ambrosio.ApiTests.Helpers;

/// <summary>Resposta de uma chamada JSON-RPC ao <c>/mcp</c>.</summary>
public sealed class McpCallResult
{
    public int StatusCode { get; init; }

    public string? WwwAuthenticate { get; init; }

    public string RawBody { get; init; } = string.Empty;

    /// <summary><c>result</c> do JSON-RPC, quando houve.</summary>
    public JsonElement? Result { get; init; }

    /// <summary><c>error</c> do JSON-RPC, quando houve.</summary>
    public JsonElement? Error { get; init; }

    public bool IsToolError =>
        Result is { } result && result.TryGetProperty("isError", out var isError) && isError.GetBoolean();

    /// <summary>Primeiro item <c>text</c> do resultado de tool.</summary>
    public string? Text =>
        Result is { } result && result.TryGetProperty("content", out var content)
            ? content.EnumerateArray().Where(c => c.GetProperty("type").GetString() == "text")
                .Select(c => c.GetProperty("text").GetString()).FirstOrDefault()
            : null;

    public JsonElement? StructuredContent =>
        Result is { } result && result.TryGetProperty("structuredContent", out var structured) ? structured : null;

    /// <summary>Primeiro valor de <c>_meta["mcp/www_authenticate"]</c> (desafio de tool).</summary>
    public string? ToolChallenge =>
        Result is { } result
        && result.TryGetProperty("_meta", out var meta)
        && meta.TryGetProperty("mcp/www_authenticate", out var challenges)
            ? challenges.EnumerateArray().Select(c => c.GetString()).FirstOrDefault()
            : null;
}

/// <summary>
/// Cliente JSON-RPC mínimo para o <c>/mcp</c> (feature 007, T028): transporte HTTP streamable sem
/// estado, aceitando resposta em JSON ou em SSE, com acesso ao status HTTP e ao
/// <c>WWW-Authenticate</c> — que é justamente o que os testes de autenticação conferem.
/// </summary>
public sealed class McpTestClient(ApiTestFixture fixture)
{
    private int _nextId;

    public Task<McpCallResult> ListToolsAsync(string? token = null) =>
        SendAsync("tools/list", new { }, token);

    public Task<McpCallResult> CallToolAsync(string name, object? arguments = null, string? token = null) =>
        SendAsync("tools/call", new { name, arguments = arguments ?? new { } }, token);

    /// <summary>Mesma chamada com um <c>Authorization</c> arbitrário (token adulterado, HS256 da API).</summary>
    public Task<McpCallResult> SendWithRawAuthorizationAsync(string method, object parameters, string authorization) =>
        SendAsync(method, parameters, token: null, rawAuthorization: authorization);

    public async Task<McpCallResult> SendAsync(string method, object parameters, string? token, string? rawAuthorization = null)
    {
        var request = new Url($"{fixture.McpBaseUrl.TrimEnd('/')}/mcp")
            .WithTimeout(fixture.TimeoutSeconds)
            .WithHeader("Accept", "application/json, text/event-stream")
            .AllowAnyHttpStatus();

        if (rawAuthorization is not null)
        {
            request = request.WithHeader("Authorization", rawAuthorization);
        }
        else if (!string.IsNullOrEmpty(token))
        {
            request = request.WithOAuthBearerToken(token);
        }

        var response = await request.PostJsonAsync(new
        {
            jsonrpc = "2.0",
            id = Interlocked.Increment(ref _nextId),
            method,
            @params = parameters
        });

        response.Headers.TryGetFirst("WWW-Authenticate", out var challenge);
        var body = await response.GetStringAsync();

        var json = ExtractJson(body);
        JsonElement? result = null;
        JsonElement? error = null;

        if (json is not null)
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("result", out var r))
            {
                result = r.Clone();
            }

            if (document.RootElement.TryGetProperty("error", out var e))
            {
                error = e.Clone();
            }
        }

        return new McpCallResult
        {
            StatusCode = response.StatusCode,
            WwwAuthenticate = challenge,
            RawBody = body,
            Result = result,
            Error = error
        };
    }

    /// <summary>Corpo JSON puro, ou o último <c>data:</c> de um stream SSE.</summary>
    private static string? ExtractJson(string body)
    {
        var trimmed = body.TrimStart();
        if (trimmed.StartsWith('{'))
        {
            return trimmed;
        }

        return body.Split('\n')
            .Where(line => line.StartsWith("data:", StringComparison.Ordinal))
            .Select(line => line["data:".Length..].Trim())
            .LastOrDefault(line => line.StartsWith('{'));
    }
}
