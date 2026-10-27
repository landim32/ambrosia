using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Ambrosia.Domain.Exceptions;
using Ambrosia.DTO.Images;
using Ambrosia.DTO.Settings;
using Ambrosia.Infra.Interfaces.AppServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ambrosia.Infra.AppServices;

/// <summary>
/// Storage de objetos compatível com S3 (research §1 a §5, §10, §17). Único ponto do backend que
/// conhece o AWS SDK: trocar o provedor é trocar <c>ObjectStorage__ServiceUrl</c>/<c>Region</c>/
/// <c>Bucket</c>/<c>ForcePathStyle</c> (FR-002), sem tocar regra de negócio nenhuma.
/// <para>
/// <b>Dois clientes</b> (research §3): <c>_client</c> faz as operações no endereço interno e
/// <c>_signer</c> só assina URLs no endereço público, porque a assinatura SigV4 cobre o
/// <c>Host</c> — uma URL assinada para um host que o navegador não resolve seria inútil, e reescrever
/// o host depois invalidaria a assinatura. Quando os dois endereços coincidem (Production, e o caso
/// comum sem <c>PublicServiceUrl</c>), usa-se um cliente só.
/// </para>
/// <para>
/// <b>Checksums</b> em <c>WHEN_REQUIRED</c> (research §2): o padrão <c>WHEN_SUPPORTED</c> do SDK 4.x
/// envia <c>x-amz-checksum-*</c> em toda escrita, e serviços compatíveis que não implementam os
/// algoritmos novos recusam o upload. Assim o checksum fica só onde a própria API S3 exige (ex.:
/// <c>DeleteObjects</c>).
/// </para>
/// <para>
/// <b>Nunca</b> define ACL pública (FR-004) e <b>nunca</b> coloca endpoint, bucket, chave, URL
/// assinada ou credencial no <c>Message</c> do erro que sobe: a exceção original vai apenas como
/// <c>InnerException</c> de <see cref="StorageUnavailableException"/>, cuja mensagem é fixa (FR-021).
/// </para>
/// </summary>
public class ObjectStorageAppService : IObjectStorageAppService, IDisposable
{
    /// <summary>Tamanho máximo do lote de <c>DeleteObjects</c> (limite da API S3).</summary>
    private const int DELETE_BATCH_SIZE = 1000;

    /// <summary>Código de erro S3 para objeto inexistente em um GET/HEAD.</summary>
    private const string NO_SUCH_KEY = "NoSuchKey";

    private readonly IAmazonS3 _client;

    /// <summary>Cliente usado apenas por <c>GetPreSignedURL</c>; pode ser o mesmo <see cref="_client"/>.</summary>
    private readonly IAmazonS3 _signer;

    private readonly ObjectStorageSettings _settings;
    private readonly ILogger<ObjectStorageAppService> _logger;

    public ObjectStorageAppService(IOptions<ObjectStorageSettings> options, ILogger<ObjectStorageAppService> logger)
    {
        _settings = options.Value;
        _logger = logger;

        _client = BuildClient(_settings.ServiceUrl);

        var publicUrl = _settings.EffectivePublicServiceUrl;
        var sameEndpoint = string.Equals(publicUrl, _settings.ServiceUrl, StringComparison.OrdinalIgnoreCase);

        _signer = sameEndpoint ? _client : BuildClient(publicUrl);
    }

    /// <inheritdoc />
    public async Task PutAsync(string key, Stream content, string contentType, CancellationToken ct = default)
    {
        await GuardAsync("PutObject", async () =>
        {
            await _client.PutObjectAsync(new PutObjectRequest
            {
                BucketName = _settings.Bucket,
                Key = key,
                InputStream = content,
                // O stream pertence ao chamador (ele o limita a MaxUploadBytes); sem isto o SDK o
                // fecharia no fim da operação e o chamador perderia o conteúdo.
                AutoCloseStream = false,
                ContentType = contentType
            }, ct);

            return true;
        }, ct);
    }

    /// <inheritdoc />
    public async Task<ObjectHead?> HeadAsync(string key, CancellationToken ct = default)
    {
        try
        {
            var meta = await _client.GetObjectMetadataAsync(_settings.Bucket, key, ct);

            return new ObjectHead
            {
                ContentLength = meta.ContentLength,
                ContentType = meta.ContentType
            };
        }
        catch (AmazonS3Exception ex) when (IsNotFound(ex))
        {
            // 404 não é indisponibilidade: é "o arquivo ainda não foi enviado" (research §6).
            return null;
        }
        catch (Exception ex) when (IsStorageFailure(ex, ct))
        {
            throw await UnavailableAsync("HeadObject", ex);
        }
    }

    /// <inheritdoc />
    public async Task<Stream> OpenReadAsync(string key, CancellationToken ct = default)
    {
        return await GuardAsync("GetObject", async () =>
        {
            var response = await _client.GetObjectAsync(_settings.Bucket, key, ct);

            // O ResponseStream É o corpo da resposta: devolver a resposta e deixá-la ser coletada
            // não a fecha precocemente (o SDK não tem finalizer de descarte aqui).
            return response.ResponseStream;
        }, ct);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string key, CancellationToken ct = default)
    {
        try
        {
            await _client.DeleteObjectAsync(_settings.Bucket, key, ct);
        }
        catch (AmazonS3Exception ex) when (IsNotFound(ex))
        {
            // Idempotente: apagar o que já não existe não é erro (a exclusão da recusa e a do job
            // de limpeza competem naturalmente com um DELETE do dono).
        }
        catch (Exception ex) when (IsStorageFailure(ex, ct))
        {
            throw await UnavailableAsync("DeleteObject", ex);
        }
    }

    /// <inheritdoc />
    public async Task DeletePrefixAsync(string prefix, CancellationToken ct = default)
    {
        string? continuationToken = null;

        do
        {
            var listed = await GuardAsync("ListObjectsV2", async () => await _client.ListObjectsV2Async(
                new ListObjectsV2Request
                {
                    BucketName = _settings.Bucket,
                    Prefix = prefix,
                    ContinuationToken = continuationToken
                }, ct), ct);

            if (listed.S3Objects.Count > 0)
            {
                // Lotes de 1000: é o limite do DeleteObjects, e o SDK calcula o checksum do corpo
                // porque a API S3 o exige — mesmo com RequestChecksumCalculation = WHEN_REQUIRED.
                foreach (var batch in listed.S3Objects.Chunk(DELETE_BATCH_SIZE))
                {
                    await GuardAsync("DeleteObjects", async () =>
                    {
                        await _client.DeleteObjectsAsync(new DeleteObjectsRequest
                        {
                            BucketName = _settings.Bucket,
                            Quiet = true,
                            Objects = batch
                                .Select(s3 => new KeyVersion { Key = s3.Key })
                                .ToList()
                        }, ct);

                        return true;
                    }, ct);
                }
            }

            // IsTruncated chega como bool? no SDK 4.x (ausente em resposta sem mais páginas).
            continuationToken = listed.IsTruncated == true ? listed.NextContinuationToken : null;
        }
        while (continuationToken is not null);
    }

    /// <inheritdoc />
    public PresignedUrl GetPresignedReadUrl(string key, DateTime now) =>
        Sign(HttpVerb.GET, key, contentType: null, now, _settings.PresignedReadMinutes);

    /// <inheritdoc />
    public PresignedUrl GetPresignedUploadUrl(string key, string contentType, DateTime now) =>
        Sign(HttpVerb.PUT, key, contentType, now, _settings.PresignedUploadMinutes);

    /// <summary>
    /// Assina localmente (sem chamada de rede, por isso o método é síncrono — research §5) no
    /// endereço público, com o <c>Content-Type</c> entre os cabeçalhos assinados quando houver: é
    /// isso que faz um <c>PUT</c> com tipo diferente receber 403 do storage (FR-003).
    /// </summary>
    private PresignedUrl Sign(HttpVerb verb, string key, string? contentType, DateTime now, int minutes)
    {
        // IClock devolve Kind=Unspecified (exigência das colunas timestamp sem fuso). A assinatura
        // trata o valor como UTC, então o Kind é explicitado antes de entregar ao SDK.
        var utcNow = DateTime.SpecifyKind(now, DateTimeKind.Utc);
        var expiresAt = utcNow.AddMinutes(minutes);

        var request = new GetPreSignedUrlRequest
        {
            BucketName = _settings.Bucket,
            Key = key,
            Verb = verb,
            Expires = expiresAt,
            ContentType = contentType,
            // A assinatura ecoa o esquema do endereço público: http para servidor local, https para
            // Spaces/S3 (research §4).
            Protocol = new Uri(_settings.EffectivePublicServiceUrl).Scheme.Equals("http", StringComparison.OrdinalIgnoreCase)
                ? Protocol.HTTP
                : Protocol.HTTPS
        };

        return new PresignedUrl
        {
            Url = _signer.GetPreSignedURL(request),
            ExpiresAt = DateTime.SpecifyKind(expiresAt, DateTimeKind.Unspecified)
        };
    }

    private IAmazonS3 BuildClient(string serviceUrl) =>
        new AmazonS3Client(
            new BasicAWSCredentials(_settings.AccessKey, _settings.SecretKey),
            new AmazonS3Config
            {
                ServiceURL = serviceUrl,
                ForcePathStyle = _settings.ForcePathStyle,
                AuthenticationRegion = _settings.Region,
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED
            });

    private static bool IsNotFound(AmazonS3Exception ex) =>
        ex.StatusCode == HttpStatusCode.NotFound || ex.ErrorCode == NO_SUCH_KEY;

    /// <summary>
    /// O que conta como storage indisponível (research §11): recusa de serviço, falha de transporte,
    /// timeout e erro do próprio SDK ao montar/falar a requisição
    /// (<see cref="Amazon.Runtime.AmazonClientException"/>). Os quatro entram no invólucro de
    /// mensagem fixa justamente porque o <c>Message</c> deles pode trazer endpoint, bucket e chave do
    /// objeto — e o handler genérico dos controllers devolve <c>ex.Message</c> ao cliente no 500, que
    /// seria o caminho de vazamento (FR-021). Cancelamento do chamador fica de fora: não é pane do
    /// storage.
    /// </summary>
    private static bool IsStorageFailure(Exception ex, CancellationToken ct) =>
        !ct.IsCancellationRequested
        && (ex is AmazonServiceException
            || ex is AmazonClientException
            || ex is HttpRequestException
            || ex is TaskCanceledException);

    /// <summary>
    /// Registra a falha (sem detalhes do storage) e devolve a exceção de domínio de mensagem fixa,
    /// para o 500/503 do controller nunca revelar endpoint, bucket ou chave (FR-021).
    /// </summary>
    private async Task<StorageUnavailableException> UnavailableAsync(string operation, Exception ex)
    {
        // Log só da operação e da exceção original; nada de credencial ou URL assinada aqui (research §17).
        _logger.LogWarning(ex, "Operacao {Operation} no storage de imagens falhou.", operation);

        await Task.CompletedTask;

        return new StorageUnavailableException(ex);
    }

    private async Task<T> GuardAsync<T>(string operation, Func<Task<T>> action, CancellationToken ct)
    {
        try
        {
            return await action();
        }
        catch (Exception ex) when (IsStorageFailure(ex, ct))
        {
            throw await UnavailableAsync(operation, ex);
        }
    }

    public void Dispose()
    {
        _client.Dispose();

        // O cliente de assinatura pode ser o mesmo objeto (sem PublicServiceUrl): nunca descartar duas vezes.
        if (!ReferenceEquals(_signer, _client))
        {
            _signer.Dispose();
        }

        GC.SuppressFinalize(this);
    }
}
