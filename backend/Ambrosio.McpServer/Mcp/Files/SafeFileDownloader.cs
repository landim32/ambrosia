using System.Net;
using System.Net.Sockets;
using Ambrosio.DTO.Settings;
using Ambrosio.McpServer.Auth.Cimd;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Options;

namespace Ambrosio.McpServer.Mcp.Files;

/// <summary>Arquivo baixado para a tool <c>enviar_imagem</c>: bytes, tipo e nome.</summary>
public sealed record DownloadedFile(byte[] Content, string MimeType, string? FileName);

/// <summary>
/// Download do <c>download_url</c> que o ChatGPT entrega para arquivos da conversa (feature 007, T066).
/// Só <c>https</c>, conexão recusada para IP privado, loopback e link-local (conferido no IP
/// resolvido), sem redirect, <i>timeout</i> de 10 s e leitura limitada a
/// <c>ObjectStorage:MaxUploadBytes</c> — o mesmo teto do storage.
/// </summary>
public sealed class SafeFileDownloader : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly HttpClient _http;
    private readonly IOptions<ObjectStorageSettings> _storage;

    public SafeFileDownloader(IOptions<ObjectStorageSettings> storage)
    {
        _storage = storage;
        _http = new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            ConnectTimeout = Timeout,
            ConnectCallback = ConnectToPublicAddressAsync
        })
        {
            Timeout = Timeout
        };
    }

    /// <exception cref="ValidationException">URL recusada ou download que não completou.</exception>
    /// <exception cref="Domain.Exceptions.ImageRejectedException">Arquivo acima do limite.</exception>
    public async Task<DownloadedFile> DownloadAsync(string? url, string? declaredMimeType, string? fileName, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw Invalid("O download_url precisa ser uma URL https.");
        }

        if (uri.IsLoopback || (IPAddress.TryParse(uri.Host, out var literal) && !CimdDocumentFetcher.IsPublic(literal)))
        {
            throw Invalid("O download_url aponta para um endereço não permitido.");
        }

        HttpResponseMessage response;
        try
        {
            response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw Invalid(ex.InnerException is BlockedAddressException
                ? "O download_url aponta para um endereço não permitido."
                : "Não foi possível baixar o arquivo do download_url.");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw Invalid($"O download_url respondeu {(int)response.StatusCode}.");
            }

            var max = _storage.Value.MaxUploadBytes;
            if (response.Content.Headers.ContentLength > max)
            {
                throw new Domain.Exceptions.ImageRejectedException(Domain.Exceptions.ImageRejectionReason.Size);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;

            while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
            {
                if (buffer.Length + read > max)
                {
                    throw new Domain.Exceptions.ImageRejectedException(Domain.Exceptions.ImageRejectionReason.Size);
                }

                buffer.Write(chunk, 0, read);
            }

            // O tipo declarado pelo ChatGPT vale mais que o Content-Type do armazenamento dele, que às
            // vezes vem genérico (application/octet-stream). O ImageService confere contra a lista.
            var mime = !string.IsNullOrWhiteSpace(declaredMimeType)
                ? declaredMimeType
                : response.Content.Headers.ContentType?.MediaType ?? string.Empty;

            return new DownloadedFile(buffer.ToArray(), mime, fileName);
        }
    }

    public void Dispose() => _http.Dispose();

    private static ValidationException Invalid(string message) =>
        new([new ValidationFailure("download_url", message)]);

    private static async ValueTask<Stream> ConnectToPublicAddressAsync(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
        var target = addresses.FirstOrDefault(CimdDocumentFetcher.IsPublic) ?? throw new BlockedAddressException();

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

    /// <summary>Nome que só resolve para endereço privado, loopback ou link-local.</summary>
    private sealed class BlockedAddressException() : Exception("Endereço não permitido.");
}
