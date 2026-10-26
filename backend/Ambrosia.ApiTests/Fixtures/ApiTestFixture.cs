using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ambrosia.DTO.Auth;
using Ambrosia.DTO.Users;
using Flurl;
using Flurl.Http;
using Microsoft.Extensions.Configuration;

namespace Ambrosia.ApiTests.Fixtures;

/// <summary>
/// Fixture da coleção de testes HTTP (skill <c>dotnet-test-api</c>), com o preset adaptado ao
/// dev-login desta feature (research §2): autentica <b>dois</b> usuários por sessão para que os
/// testes de isolamento (US4) tenham A e B disponíveis.
///
/// O dev-login não usa senha, então <c>appsettings.Test.json</c> não contém segredos e não há
/// placeholders <c>REPLACE_VIA_ENV_*</c>.
/// </summary>
public class ApiTestFixture : IAsyncLifetime
{
    /// <summary>Leitura de JSON dos testes: camelCase + enums como string (contrato da API).</summary>
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public string BaseUrl { get; private set; } = string.Empty;

    /// <summary>Token do usuário A ("Ana") — dono dos recursos criados nos testes.</summary>
    public string TokenA { get; private set; } = string.Empty;

    /// <summary>Token do usuário B ("Bia") — intruso nos testes de isolamento.</summary>
    public string TokenB { get; private set; } = string.Empty;

    public long UserIdA { get; private set; }

    public long UserIdB { get; private set; }

    /// <summary>E-mail do usuário A desta sessão (sufixo GUID: cada execução cria usuários novos).</summary>
    public string EmailA { get; private set; } = string.Empty;

    /// <summary>E-mail do usuário B desta sessão.</summary>
    public string EmailB { get; private set; } = string.Empty;

    public int TimeoutSeconds { get; private set; } = 30;

    public string DevLoginEndpoint { get; private set; } = "/auth/dev-login";

    private IConfiguration _configuration = null!;

    public async Task InitializeAsync()
    {
        _configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.Test.json", optional: false)
            .AddEnvironmentVariables()
            .Build();

        BaseUrl = RequireConfig("ApiBaseUrl");
        DevLoginEndpoint = _configuration["Auth:DevLoginEndpoint"] ?? "/auth/dev-login";
        TimeoutSeconds = int.TryParse(_configuration["Timeout"], out var timeout) ? timeout : 30;

        await EnsureApiIsUp();

        // Sufixo único por sessão: evita conflito com execuções anteriores no banco de dev.
        var suffix = Guid.NewGuid().ToString("N")[..8];
        EmailA = $"ana+{suffix}@exemplo.com";
        EmailB = $"bia+{suffix}@exemplo.com";

        var loginA = await DevLoginAsync(EmailA, "Ana");
        TokenA = loginA.Token;
        UserIdA = loginA.User.Id;

        var loginB = await DevLoginAsync(EmailB, "Bia");
        TokenB = loginB.Token;
        UserIdB = loginB.User.Id;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>Requisição autenticada com um token específico.</summary>
    public IFlurlRequest CreateRequest(string path, string token) =>
        BuildRequest(path).WithOAuthBearerToken(token);

    /// <summary>Requisição sem cabeçalho Authorization (teste de 401).</summary>
    public IFlurlRequest CreateAnonymousRequest(string path) => BuildRequest(path);

    /// <summary>
    /// Dev-login direto, usado pelo setup do fixture e por testes que precisam de um usuário
    /// extra (ex.: o mesmo CPF para dois usuários, US4 cenário 4).
    /// </summary>
    public async Task<LoginResult> DevLoginAsync(string email, string name)
    {
        var response = await CreateAnonymousRequest(DevLoginEndpoint)
            .AllowAnyHttpStatus()
            .PostJsonAsync(new DevLoginInfo { Email = email, Name = name });

        if (response.StatusCode == 404)
        {
            throw new InvalidOperationException(
                $"POST {DevLoginEndpoint} respondeu 404 em {BaseUrl}: os testes HTTP exigem a API no ar " +
                "com ASPNETCORE_ENVIRONMENT=Development. Suba-a com backend/scripts/run-api.ps1.");
        }

        if (!response.StatusCode.IsSuccess())
        {
            var body = await response.GetStringAsync();
            throw new InvalidOperationException(
                $"Dev-login de '{email}' falhou com HTTP {response.StatusCode}: {body}");
        }

        var result = Deserialize<LoginResult>(await response.GetStringAsync());
        if (string.IsNullOrWhiteSpace(result.Token))
        {
            throw new InvalidOperationException($"Dev-login de '{email}' não retornou token.");
        }

        return result;
    }

    /// <summary>GET com resposta capturada sem exceção (status, corpo bruto, Location).</summary>
    public async Task<HttpCallResult> GetAsync(string path, string token)
    {
        var request = string.IsNullOrEmpty(token)
            ? CreateAnonymousRequest(path)
            : CreateRequest(path, token);

        return await HttpCallResult.From(await request.AllowAnyHttpStatus().GetAsync());
    }

    /// <summary>POST com resposta capturada sem exceção.</summary>
    public async Task<HttpCallResult> PostAsync(string path, string token, object payload)
    {
        var request = string.IsNullOrEmpty(token)
            ? CreateAnonymousRequest(path)
            : CreateRequest(path, token);

        return await HttpCallResult.From(await request.AllowAnyHttpStatus().PostJsonAsync(payload));
    }

    /// <summary>PUT com resposta capturada sem exceção.</summary>
    public async Task<HttpCallResult> PutAsync(string path, string token, object payload)
    {
        var request = string.IsNullOrEmpty(token)
            ? CreateAnonymousRequest(path)
            : CreateRequest(path, token);

        return await HttpCallResult.From(await request.AllowAnyHttpStatus().PutJsonAsync(payload));
    }

    /// <summary>Requisição com cabeçalho Authorization arbitrário (token adulterado/estranho).</summary>
    public IFlurlRequest CreateRequestWithRawAuthorization(string path, string authorizationHeader) =>
        BuildRequest(path).WithHeader("Authorization", authorizationHeader);

    public static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, JsonOptions)
        ?? throw new InvalidOperationException($"Resposta JSON vazia ou inválida: {json}");

    /// <summary>
    /// Monta a requisição a partir de um caminho relativo que pode carregar query string
    /// (<c>/customers?search=ana&amp;page=1</c>). Passar o caminho inteiro por
    /// <c>AppendPathSegment</c> codificaria o "?" como <c>%3F</c> e a rota não casaria (404), por
    /// isso o URL absoluto é parseado pelo Flurl, que separa caminho e query corretamente.
    /// </summary>
    private IFlurlRequest BuildRequest(string path)
    {
        var relative = path.TrimStart('/');
        var absolute = Uri.TryCreate(relative, UriKind.Absolute, out _)
            ? relative
            : $"{BaseUrl.TrimEnd('/')}/{relative}";

        return new Url(absolute).WithTimeout(TimeSpan.FromSeconds(TimeoutSeconds));
    }

    private async Task EnsureApiIsUp()
    {
        try
        {
            var response = await new Url(BaseUrl)
                .AppendPathSegment("swagger/index.html")
                .WithTimeout(TimeSpan.FromSeconds(TimeoutSeconds))
                .AllowAnyHttpStatus()
                .GetAsync();

            if (!response.StatusCode.IsSuccess())
            {
                throw new InvalidOperationException(
                    $"GET /swagger/index.html respondeu {response.StatusCode} em {BaseUrl}.");
            }
        }
        catch (FlurlHttpException ex)
        {
            throw new InvalidOperationException(
                $"A API Ambrosia não está acessível em {BaseUrl} ({ex.GetType().Name}: {ex.Message}). " +
                "Suba-a em Development antes de rodar Ambrosia.ApiTests: backend/scripts/run-api.ps1.",
                ex);
        }
    }

    private string RequireConfig(string key)
    {
        var value = _configuration[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Configuração obrigatória ausente nos testes HTTP: chave '{key}' " +
                "(appsettings.Test.json ou variável de ambiente).");
        }

        if (value.StartsWith("REPLACE_VIA_ENV_", StringComparison.Ordinal))
        {
            var envVar = value["REPLACE_VIA_ENV_".Length..];
            throw new InvalidOperationException(
                $"A chave '{key}' ainda contém o placeholder. Exporte a variável '{envVar}'.");
        }

        return value;
    }
}
