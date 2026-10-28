using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ambrosio.DTO.Settings;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Ambrosio.McpServer.Auth.Cimd;

/// <summary>Documento de metadados do cliente (CIMD), só com os campos que o AS usa.</summary>
public sealed class CimdDocument
{
    [JsonPropertyName("client_id")]
    public string? ClientId { get; set; }

    [JsonPropertyName("client_name")]
    public string? ClientName { get; set; }

    [JsonPropertyName("redirect_uris")]
    public List<string>? RedirectUris { get; set; }

    [JsonPropertyName("token_endpoint_auth_method")]
    public string? TokenEndpointAuthMethod { get; set; }

    [JsonPropertyName("token_endpoint_auth_methods_supported")]
    public List<string>? TokenEndpointAuthMethodsSupported { get; set; }
}

/// <summary>Recusa do documento, com a mensagem que vai para o <c>error_description</c>.</summary>
public sealed class CimdRejectedException(string message) : Exception(message);

/// <summary>
/// Baixa o documento CIMD do <c>client_id</c> (feature 007, T037, R4) com as travas de SSRF: só
/// <c>https</c>, host em <c>Auth:Cimd:AllowedHosts</c>, conexão recusada para IP privado, loopback e
/// link-local (conferido no IP resolvido, não no nome), <i>timeout</i> de 5 s e 64 KB no máximo. O
/// documento fica em cache pelo <c>Cache-Control</c>, entre 1 minuto e 1 hora.
/// </summary>
public sealed class CimdDocumentFetcher : IDisposable
{
    public const int MaxBytes = 64 * 1024;

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DefaultCache = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan MinCache = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan MaxCache = TimeSpan.FromHours(1);

    private readonly HttpClient _http;
    private readonly IMemoryCache _cache;
    private readonly IOptions<AuthServerSettings> _settings;

    public CimdDocumentFetcher(IMemoryCache cache, IOptions<AuthServerSettings> settings)
    {
        _cache = cache;
        _settings = settings;

        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            ConnectTimeout = Timeout,
            ConnectCallback = ConnectToPublicAddressAsync
        };

        _http = new HttpClient(handler) { Timeout = Timeout };
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    /// <summary>
    /// <c>client_id</c> que deve ser tratado como CIMD: URL https absoluta. Qualquer outro valor é um
    /// cliente registrado por DCR e segue o caminho normal do OpenIddict.
    /// </summary>
    public static bool IsCimdClientId(string? clientId) =>
        clientId is not null
        && clientId.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        && Uri.TryCreate(clientId, UriKind.Absolute, out _);

    public async Task<CimdDocument> GetAsync(string clientId, CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(clientId, out CimdDocument? cached) && cached is not null)
        {
            return cached;
        }

        var uri = new Uri(clientId, UriKind.Absolute);

        if (uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.Fragment) || !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new CimdRejectedException("O client_id precisa ser uma URL https sem credenciais nem fragmento.");
        }

        if (!_settings.Value.Cimd.AllowedHosts.Any(h => string.Equals(h, uri.Host, StringComparison.OrdinalIgnoreCase)))
        {
            throw new CimdRejectedException("O host do client_id não está na lista de clientes aceitos.");
        }

        HttpResponseMessage response;
        try
        {
            response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or CimdRejectedException)
        {
            throw new CimdRejectedException(ex is CimdRejectedException ? ex.Message : "Não foi possível baixar o documento do cliente.");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new CimdRejectedException($"O documento do cliente respondeu {(int)response.StatusCode}.");
            }

            if (response.Content.Headers.ContentLength > MaxBytes)
            {
                throw new CimdRejectedException("O documento do cliente excede o tamanho máximo.");
            }

            var body = await ReadLimitedAsync(response, cancellationToken);

            CimdDocument? document;
            try
            {
                document = JsonSerializer.Deserialize<CimdDocument>(body);
            }
            catch (JsonException)
            {
                throw new CimdRejectedException("O documento do cliente não é um JSON válido.");
            }

            if (document is null || !string.Equals(document.ClientId, clientId, StringComparison.Ordinal))
            {
                throw new CimdRejectedException("O client_id do documento não é igual à URL do documento.");
            }

            _cache.Set(clientId, document, CacheDuration(response.Headers.CacheControl));

            return document;
        }
    }

    public void Dispose() => _http.Dispose();

    private static async Task<byte[]> ReadLimitedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;

        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxBytes)
            {
                throw new CimdRejectedException("O documento do cliente excede o tamanho máximo.");
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static TimeSpan CacheDuration(CacheControlHeaderValue? cacheControl)
    {
        if (cacheControl is { NoStore: true } or { NoCache: true })
        {
            return MinCache;
        }

        var maxAge = cacheControl?.MaxAge ?? DefaultCache;

        return maxAge < MinCache ? MinCache : maxAge > MaxCache ? MaxCache : maxAge;
    }

    /// <summary>
    /// Resolve o nome e conecta só em endereço público. A checagem é no IP de verdade, depois do DNS:
    /// um nome permitido que resolva para 10.x ou 169.254.x (rebinding) é recusado do mesmo jeito.
    /// </summary>
    private static async ValueTask<Stream> ConnectToPublicAddressAsync(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
        var target = addresses.FirstOrDefault(IsPublic)
            ?? throw new CimdRejectedException("O host do client_id não resolve para um endereço público.");

        var socket = new Socket(target.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(new IPEndPoint(target, context.DnsEndPoint.Port), cancellationToken);

            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
        {
            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return !(address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast
                     || address.IsIPv6UniqueLocal);
        }

        var b = address.GetAddressBytes();

        return !(b[0] == 10
                 || b[0] == 127
                 || b[0] == 0
                 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                 || (b[0] == 192 && b[1] == 168)
                 || (b[0] == 169 && b[1] == 254)
                 || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)
                 || b[0] >= 224);
    }
}
