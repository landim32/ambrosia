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
/// Fixture da coleção de testes HTTP (skill <c>dotnet-test-api</c>): autentica pelo caminho
/// real da aplicação — <c>POST /auth/register</c> seguido de <c>POST /auth/login</c> — e cria
/// <b>dois</b> usuários por sessão para que os testes de isolamento (US4) tenham A e B.
///
/// Nenhum teste depende de usuário semeado à mão nem de atalho sem senha: os e-mails usam um
/// sufixo GUID por execução, então a suíte pode rodar duas vezes seguidas no mesmo banco de
/// Development e continuar verde (FR-016, SC-005).
///
/// <c>Auth:TestPassword</c> <b>não é um segredo</b>: só existe em bancos de Development/Homolog
/// e cada execução cria contas novas com e-mail aleatório (research §7).
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

    public string RegisterEndpoint { get; private set; } = "/auth/register";

    public string LoginEndpoint { get; private set; } = "/auth/login";

    /// <summary>Senha padrão das contas criadas pela suíte (não é segredo — ver cabeçalho).</summary>
    public string TestPassword { get; private set; } = "Senha-de-teste-123";

    // ---------------------------------------------------------------------
    // Storage de imagens (feature 004, T027): os prazos que a suíte ESPERA encontrar no ambiente,
    // porque os testes de expiração e de limpeza precisam dormir exatamente esse tempo. Vêm do
    // appsettings da API sob teste (homolog usa valores curtos de propósito — research §13); quando
    // a espera ultrapassaria Storage:SlowTestMaxWaitMinutes, o teste se auto-pula (research §16).
    // ---------------------------------------------------------------------

    /// <summary>Validade esperada das URLs pré-assinadas de leitura ( minutos).</summary>
    public int StoragePresignedReadMinutes { get; private set; } = 1;

    /// <summary>Validade esperada das URLs pré-assinadas de envio (minutos).</summary>
    public int StoragePresignedUploadMinutes { get; private set; } = 1;

    /// <summary>Idade a partir da qual pendentes e falhas são limpas (minutos).</summary>
    public int StoragePendingTtlMinutes { get; private set; } = 2;

    /// <summary>Intervalo entre varreduras do job de limpeza (minutos).</summary>
    public int StorageCleanupIntervalMinutes { get; private set; } = 1;

    /// <summary>Limite de upload esperado, em bytes.</summary>
    public long StorageMaxUploadBytes { get; private set; } = 10_485_760;

    /// <summary>Teto de espera de um teste lento: acima disso ele é pulado (research §16).</summary>
    public int SlowTestMaxWaitMinutes { get; private set; } = 5;

    /// <summary>Origem do frontend, esperada no <c>Access-Control-Allow-Origin</c> (FR-022).</summary>
    public string StorageFrontendOrigin { get; private set; } = "http://localhost:8080";

    private IConfiguration _configuration = null!;

    public async Task InitializeAsync()
    {
        _configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.Test.json", optional: false)
            .AddEnvironmentVariables()
            .Build();

        BaseUrl = RequireConfig("ApiBaseUrl");
        RegisterEndpoint = _configuration["Auth:RegisterEndpoint"] ?? "/auth/register";
        LoginEndpoint = _configuration["Auth:LoginEndpoint"] ?? "/auth/login";
        TestPassword = _configuration["Auth:TestPassword"] ?? "Senha-de-teste-123";
        TimeoutSeconds = int.TryParse(_configuration["Timeout"], out var timeout) ? timeout : 30;

        StoragePresignedReadMinutes = ReadInt("Storage:PresignedReadMinutes", StoragePresignedReadMinutes);
        StoragePresignedUploadMinutes = ReadInt("Storage:PresignedUploadMinutes", StoragePresignedUploadMinutes);
        StoragePendingTtlMinutes = ReadInt("Storage:PendingTtlMinutes", StoragePendingTtlMinutes);
        StorageCleanupIntervalMinutes = ReadInt("Storage:CleanupIntervalMinutes", StorageCleanupIntervalMinutes);
        SlowTestMaxWaitMinutes = ReadInt("Storage:SlowTestMaxWaitMinutes", SlowTestMaxWaitMinutes);
        StorageFrontendOrigin = _configuration["Storage:FrontendOrigin"] ?? StorageFrontendOrigin;

        if (long.TryParse(_configuration["Storage:MaxUploadBytes"], out var maxUploadBytes))
        {
            StorageMaxUploadBytes = maxUploadBytes;
        }

        await EnsureApiIsUp();

        // Sufixo único por sessão: evita conflito com execuções anteriores no banco de dev.
        var suffix = Guid.NewGuid().ToString("N")[..8];
        EmailA = $"ana+{suffix}@exemplo.com";
        EmailB = $"bia+{suffix}@exemplo.com";

        var loginA = await CreateUserAsync(EmailA, "Ana");
        TokenA = loginA.Token;
        UserIdA = loginA.User.Id;

        var loginB = await CreateUserAsync(EmailB, "Bia");
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
    /// <c>POST /auth/register</c> exigindo 201. Usado pelo setup e por testes que precisam de
    /// uma conta sem token na mão (ex.: a rota pública anônima de <c>/auth/login</c>).
    /// </summary>
    public async Task<LoginResult> RegisterAsync(string email, string name, string password)
    {
        var response = await CreateAnonymousRequest(RegisterEndpoint)
            .AllowAnyHttpStatus()
            .PostJsonAsync(new RegisterInfo { Email = email, Name = name, Password = password });

        if (response.StatusCode != 201)
        {
            var body = await response.GetStringAsync();
            throw new InvalidOperationException(
                $"POST {RegisterEndpoint} de '{email}' respondeu {response.StatusCode} (esperado 201): {body}. " +
                "Confirme a API no ar em Development (backend/scripts/run-api.ps1) e se a rota " +
                "/auth/register existe nesta build.");
        }

        var result = Deserialize<LoginResult>(await response.GetStringAsync());
        if (string.IsNullOrWhiteSpace(result.Token))
        {
            throw new InvalidOperationException($"Cadastro de '{email}' não retornou token.");
        }

        return result;
    }

    /// <summary><c>POST /auth/login</c> exigindo 200 e token não vazio.</summary>
    public async Task<LoginResult> LoginAsync(string email, string password)
    {
        var response = await CreateAnonymousRequest(LoginEndpoint)
            .AllowAnyHttpStatus()
            .PostJsonAsync(new LoginInfo { Email = email, Password = password });

        if (response.StatusCode != 200)
        {
            var body = await response.GetStringAsync();
            throw new InvalidOperationException(
                $"POST {LoginEndpoint} de '{email}' respondeu {response.StatusCode} (esperado 200): {body}.");
        }

        var result = Deserialize<LoginResult>(await response.GetStringAsync());
        if (string.IsNullOrWhiteSpace(result.Token))
        {
            throw new InvalidOperationException($"Login de '{email}' não retornou token.");
        }

        return result;
    }

    /// <summary>
    /// Cria a conta e entra na sequência (US4 cenário 1): o <see cref="LoginResult"/> devolvido é
    /// o do <b>login</b>, então todo teste autenticado prova que a senha cadastrada funciona —
    /// não que um atalho de desenvolvimento gerou o token.
    /// </summary>
    public async Task<LoginResult> CreateUserAsync(string email, string name)
    {
        await RegisterAsync(email, name, TestPassword);

        return await LoginAsync(email, TestPassword);
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

    /// <summary>
    /// DELETE com resposta capturada (feature 004: <c>DELETE /images/{imageId}</c> devolve 204 sem
    /// corpo, então o <see cref="HttpCallResult"/> precisa ser lido mesmo assim).
    /// </summary>
    public async Task<HttpCallResult> DeleteAsync(string path, string token)
    {
        var request = string.IsNullOrEmpty(token)
            ? CreateAnonymousRequest(path)
            : CreateRequest(path, token);

        return await HttpCallResult.From(await request.AllowAnyHttpStatus().DeleteAsync());
    }

    /// <summary>
    /// POST <c>multipart/form-data</c> com um arquivo (feature 004, <c>POST /images</c>). O nome do
    /// campo é o que a API lê (<c>file</c>); enviar bytes direto é o que permite testar o PDF disfarçado
    /// de <c>.jpg</c> e o corpo vazio.
    /// </summary>
    /// <param name="path">Caminho relativo da rota.</param>
    /// <param name="token">JWT; string vazia envia sem cabeçalho Authorization.</param>
    /// <param name="content">Bytes do arquivo.</param>
    /// <param name="contentType">Tipo declarado no multipart.</param>
    /// <param name="fileName">Nome do arquivo no parts (name=... filename=...).</param>
    /// <param name="fieldOverride">Nome do campo; <c>null</c> = <c>file</c>. Passar <c>null</c> com
    /// <paramref name="content"/> vazio simula "campo ausente".</param>
    public async Task<HttpCallResult> PostFileAsync(
        string path,
        string token,
        byte[]? content,
        string contentType,
        string fileName,
        string? fieldOverride = "file")
    {
        var request = string.IsNullOrEmpty(token)
            ? CreateAnonymousRequest(path)
            : CreateRequest(path, token);

        return await HttpCallResult.From(await request.AllowAnyHttpStatus().PostMultipartAsync(capture =>
        {
            // Flurl 4 usa Stream no multipart; o nome do campo e o fileName vão no Content-Disposition.
            if (fieldOverride is not null && content is not null)
            {
                capture.AddFile(fieldOverride, new MemoryStream(content), contentType, fileName);
            }
        }));
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

    /// <summary>Inteiro de configuração com padrão, para os prazos esperados do storage.</summary>
    private int ReadInt(string key, int fallback) =>
        int.TryParse(_configuration[key], out var value) ? value : fallback;

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
