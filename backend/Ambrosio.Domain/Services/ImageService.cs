using System.Security.Cryptography;
using Ambrosio.Domain.Exceptions;
using Ambrosio.Domain.Interfaces;
using Ambrosio.Domain.Models;
using Ambrosio.DTO.Enums;
using Ambrosio.DTO.Images;
using Ambrosio.DTO.Settings;
using Ambrosio.Infra.Interfaces.AppServices;
using Ambrosio.Infra.Interfaces.Repository;
using FluentValidation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ambrosio.Domain.Services;

/// <summary>
/// Regras de vida da imagem (FR-003 a FR-018). Fala com o storage apenas pela abstração da Infra,
/// então nenhuma troca de provedor alcança este arquivo.
/// <para>
/// As URLs pré-assinadas são geradas <b>a cada resposta</b> e nunca persistidas (FR-012, FR-013);
/// <see cref="MapToInfo"/> só as produz para imagem <c>available</c> (FR-015). O log menciona no máximo
/// <c>imageId</c> e <c>userId</c>: chave do objeto, endpoint, URL assinada e credencial ficam de fora
/// (FR-021).
/// </para>
/// </summary>
public class ImageService : IImageService
{
    /// <summary>Mensagem única de ausência — a mesma para "não existe" e "é de outro usuário" (SC-008).</summary>
    private const string ImageNotFound = "Imagem não encontrada.";

    /// <summary>Registros tratados por varredura do job de limpeza (research §9).</summary>
    private const int StaleBatchSize = 200;

    /// <summary>Tamanho do buffer de cópia do stream do storage (80 KB, como o padrão do SDK).</summary>
    private const int CopyBufferSize = 81920;

    private readonly IStoredImageRepository<StoredImage> _images;
    private readonly IObjectStorageAppService _storage;
    private readonly IValidator<ImageUploadInsertInfo> _uploadValidator;
    private readonly ObjectStorageSettings _settings;
    private readonly IClock _clock;
    private readonly ILogger<ImageService> _logger;

    /// <summary>
    /// Usado <b>só</b> em <see cref="DeleteAllForUserAsync"/>: as fotos das clientes apontam para estas
    /// imagens por FK sem cascata (feature 009, research R13), então precisam sair da frente antes dos
    /// metadados de imagem. Nenhuma outra operação desta classe o toca.
    /// </summary>
    private readonly ICustomerPhotoRepository<CustomerPhoto> _photoRepo;

    public ImageService(
        IStoredImageRepository<StoredImage> images,
        IObjectStorageAppService storage,
        IOptions<ObjectStorageSettings> settings,
        IValidator<ImageUploadInsertInfo> uploadValidator,
        ICustomerPhotoRepository<CustomerPhoto> photoRepo,
        IClock clock,
        ILogger<ImageService> logger)
    {
        _images = images;
        _storage = storage;
        _settings = settings.Value;
        _uploadValidator = uploadValidator;
        _photoRepo = photoRepo;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<(ImageUploadResult Result, bool Created)> RequestUploadAsync(long userId, ImageUploadInsertInfo info)
    {
        await ThrowIfInvalidAsync(info);

        var sha256 = NormalizeSha256(info.Sha256);
        var mimeType = NormalizeMimeType(info.MimeType);
        var now = _clock.UtcNow;

        var existing = await _images.GetActiveByHashAsync(userId, sha256);

        if (existing is not null)
        {
            // Available: nada a enviar (200, alreadyExists). Pending: reaproveita o registro com URL
            // nova (201) — o envio anterior pode ter expirado no navegador sem gravar nada.
            return (BuildUploadResult(existing, mimeType, now), Created: existing.Status != ImageStatus.Available);
        }

        var pending = StoredImage.CreatePending(userId, sha256, mimeType, info.SizeBytes, ImageSource.Upload, now);

        try
        {
            pending = await _images.InsertAsync(pending);
        }
        catch (DuplicateException)
        {
            // Corrida (research §8): outro pedido inseriu o mesmo (usuário, hash) ativo entre a nossa
            // leitura e o nosso INSERT. O índice único parcial decidiu o vencedor; relê e atende igual.
            var winner = await _images.GetActiveByHashAsync(userId, sha256)
                ?? throw new NotFoundException(ImageNotFound);

            _logger.LogInformation(
                "Corrida de declaração resolvida pela imagem {ImageId} do usuário {UserId}.",
                winner.Id, userId);

            return (BuildUploadResult(winner, mimeType, now), Created: winner.Status != ImageStatus.Available);
        }

        _logger.LogInformation(
            "Envio declarado: imagem {ImageId} do usuário {UserId}, {SizeBytes} bytes.",
            pending.Id, userId, pending.SizeBytes);

        return (BuildUploadResult(pending, mimeType, now), Created: true);
    }

    /// <inheritdoc />
    public async Task<ImageInfo> ConfirmUploadAsync(long userId, long imageId)
    {
        var image = await _images.GetByIdAsync(imageId, userId) ?? throw new NotFoundException(ImageNotFound);

        var now = _clock.UtcNow;

        if (image.Status == ImageStatus.Available)
        {
            // Idempotência (US1 cenário 3): confirmar de novo não reescreve nada, só gera URL nova.
            return MapToInfo(image);
        }

        if (image.Status == ImageStatus.Failed)
        {
            throw new ImageRejectedException(ImageRejectionReason.AlreadyFailed);
        }

        // 1) Tamanho antes do download: um objeto declarado pequeno e substituído por um gigante nunca
        // é baixado inteiro (research §6).
        var head = await _storage.HeadAsync(image.StorageKey);

        if (head is null)
        {
            // Sem objeto: continua pending, porque o cliente ainda pode enviar (contrato images-api).
            throw new ImageRejectedException(ImageRejectionReason.NotUploaded);
        }

        if (head.ContentLength != image.SizeBytes || head.ContentLength > _settings.MaxUploadBytes)
        {
            throw await RejectAsync(image, ImageRejectionReason.Size, now);
        }

        // 2) Conteúdo real, com cópia limitada — o Head só aponta o tamanho que o storage reporta.
        await using var source = await _storage.OpenReadAsync(image.StorageKey);

        using var content = await CopyBoundedAsync(source)
            ?? throw await RejectAsync(image, ImageRejectionReason.Size, now);

        // 3) Hash: é a única prova de que o binário é o que foi declarado (critério 6 da HU 003). O
        // tipo não é relido do conteúdo: a declaração já o restringiu aos aceitos e o Content-Type
        // está assinado na URL de envio (FR-003).
        if (Sha256Hex(content.ToArray()) != image.Sha256)
        {
            throw await RejectAsync(image, ImageRejectionReason.Hash, now);
        }

        // Sem processador de imagem, as dimensões ficam desconhecidas.
        image.MarkAvailable(width: null, height: null, now);
        await _images.UpdateAsync(image);

        _logger.LogInformation("Envio aprovado: imagem {ImageId} do usuário {UserId}.", image.Id, userId);

        return MapToInfo(image);
    }

    /// <inheritdoc />
    public async Task<(ImageInfo Image, bool Created)> StoreFromServerAsync(long userId, Stream content, string mimeType, ImageSource source)
    {
        // No caminho do servidor não há declaração a conferir: tamanho e hash são medidos aqui e o
        // tipo informado tem de estar entre os aceitos (FR-008, FR-009).
        mimeType = NormalizeMimeType(mimeType);

        if (!IsAllowedMimeType(mimeType))
        {
            throw new ImageRejectedException(ImageRejectionReason.Type);
        }

        using var buffer = await CopyBoundedAsync(content) ?? throw EmptyOrTooLarge();

        if (buffer.Length == 0)
        {
            throw new ImageRejectedException(ImageRejectionReason.Size);
        }

        var bytes = buffer.ToArray();
        var sha256 = Sha256Hex(bytes);
        var now = _clock.UtcNow;

        var existing = await _images.GetActiveByHashAsync(userId, sha256);

        if (existing is not null)
        {
            if (existing.Status == ImageStatus.Available)
            {
                // Mesmo conteúdo final já disponível: nada sobe, nada se insere (FR-011).
                return (MapToInfo(existing), Created: false);
            }

            // Havia uma pendência do mesmo conteúdo (declarada pelo navegador e nunca confirmada). O
            // binário que estamos gravando é exatamente o que aquela pendência espera na mesma chave,
            // então ela é promovida em vez de abrir um segundo registro — que o índice único parcial
            // não deixaria existir.
            await PutContentAsync(existing.StorageKey, bytes, mimeType);

            existing.SizeBytes = bytes.Length;
            existing.MarkAvailable(width: null, height: null, now);

            var promoted = await _images.UpdateAsync(existing);

            _logger.LogInformation(
                "Gravação pelo servidor promoveu a pendência {ImageId} do usuário {UserId}.",
                promoted.Id, userId);

            return (MapToInfo(promoted), Created: false);
        }

        var image = StoredImage.CreateAvailable(
            userId, sha256, mimeType, bytes.Length, width: null, height: null, source, now);

        await PutContentAsync(image.StorageKey, bytes, mimeType);

        StoredImage saved;

        try
        {
            saved = await _images.InsertAsync(image);
        }
        catch (DuplicateException)
        {
            // Corrida com outro envio do mesmo conteúdo: devolve o vencedor e não deixa objeto órfão.
            var winner = await _images.GetActiveByHashAsync(userId, sha256);

            if (winner is null)
            {
                await _storage.DeleteAsync(image.StorageKey);
                throw;
            }

            return (MapToInfo(winner), Created: false);
        }
        catch
        {
            // O banco recusou por outro motivo: sem metadado o objeto ficaria inalcançável para
            // sempre (a limpeza varre registros, não o bucket). Apaga e propaga (T045).
            await _storage.DeleteAsync(image.StorageKey);
            throw;
        }

        _logger.LogInformation(
            "Imagem {ImageId} gravada pelo servidor para o usuário {UserId}.",
            saved.Id, userId);

        return (MapToInfo(saved), Created: true);
    }

    /// <inheritdoc />
    public async Task<ImageInfo> GetByIdAsync(long userId, long imageId)
    {
        var image = await _images.GetByIdAsync(imageId, userId) ?? throw new NotFoundException(ImageNotFound);

        // URL gerada a cada consulta e nunca persistida (FR-012); pending/failed ficam sem url por
        // construção em MapToInfo (FR-015).
        return MapToInfo(image);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(long userId, long imageId)
    {
        var image = await _images.GetByIdAsync(imageId, userId) ?? throw new NotFoundException(ImageNotFound);

        // Objeto antes do metadado: ao contrário, uma falha no storage deixaria binário sem registro
        // nenhum apontando para ele — e a chave é determinística, então o próximo envio do mesmo
        // conteúdo herdaria o órfão (FR-016). DeleteAsync ignora objeto inexistente, o que cobre o
        // pending que nunca chegou a enviar.
        await _storage.DeleteAsync(image.StorageKey);
        await _images.DeleteAsync(image);

        _logger.LogInformation("Imagem {ImageId} excluída pelo usuário {UserId}.", imageId, userId);
    }

    /// <inheritdoc />
    public async Task DeleteAllForUserAsync(long userId)
    {
        // FR-017: primeiro os objetos sob o prefixo da conta, depois os metadados. Sem endpoint — o
        // único chamador é o futuro fluxo de exclusão de conta.
        //
        // Antes de tudo isso, o álbum de fotos (feature 009, research R13): `customer_photos.image_id`
        // e `customers.profile_image_id` apontam para estas linhas, e as FKs são ClientSetNull — sem
        // cascata no banco. Um ExecuteDelete em stored_images com foto viva por cima daria violação de
        // FK, e o ExecuteDelete não passa pelo change tracker do EF para anular a referência.
        await _photoRepo.ClearProfilesForUserAsync(userId, _clock.UtcNow);
        await _photoRepo.DeleteAllForUserAsync(userId);

        await _storage.DeletePrefixAsync(StoredImage.BuildUserPrefix(userId));
        await _images.DeleteAllByUserAsync(userId);

        _logger.LogInformation("Imagens do usuário {UserId} removidas.", userId);
    }

    /// <inheritdoc />
    public async Task<int> PurgeStaleAsync(DateTime now)
    {
        var cutoff = now.AddMinutes(-_settings.PendingTtlMinutes);
        var removed = 0;

        // Laço até esvaziar: cada varredura trata no máximo um lote, para o job não segurar a tabela
        // inteira de uma vez (research §9).
        while (true)
        {
            var stale = await _images.ListStaleAsync(cutoff, cutoff, StaleBatchSize);

            if (stale.Count == 0)
            {
                break;
            }

            foreach (var image in stale)
            {
                if (image.Status == ImageStatus.Pending)
                {
                    // Pendente pode ter objeto (enviou e não confirmou) ou não (só declarou). Apagar o
                    // que não existe não é erro, e a chave não pode estar em uso por outra linha
                    // ativa: ux_stored_images_storage_key_active garante uma chave ativa por vez.
                    await _storage.DeleteAsync(image.StorageKey);
                }

                // Falha: o objeto já foi apagado na recusa e a MESMA chave pode estar ocupada por um
                // reenvio novo (research §8) — por isso aqui só se remove o metadado.
                await _images.DeleteAsync(image);
                removed++;
            }

            if (stale.Count < StaleBatchSize)
            {
                break;
            }
        }

        if (removed > 0)
        {
            _logger.LogInformation("Limpeza de imagens: {Removed} registros removidos.", removed);
        }

        return removed;
    }

    /// <summary>
    /// Metadado → DTO. <c>url</c> e <c>urlExpiresAt</c> só existem quando a imagem está disponível:
    /// uma URL assinada para um objeto que ainda não foi enviado ou que foi recusado não levaria a
    /// lugar nenhum, e o contrato pede <c>null</c> nesses casos (FR-015).
    /// </summary>
    private ImageInfo MapToInfo(StoredImage image)
    {
        var info = new ImageInfo
        {
            ImageId = image.Id,
            MimeType = image.MimeType,
            SizeBytes = image.SizeBytes,
            Sha256 = image.Sha256,
            Width = image.Width,
            Height = image.Height,
            Status = image.Status,
            Source = image.Source,
            CreatedAt = image.CreatedAt
        };

        if (image.Status == ImageStatus.Available)
        {
            var url = _storage.GetPresignedReadUrl(image.StorageKey, _clock.UtcNow);

            info.Url = url.Url;
            info.UrlExpiresAt = url.ExpiresAt;
        }

        return info;
    }

    /// <summary>
    /// Monta a resposta da declaração. <c>available</c> não tem nada a enviar: devolve a imagem e
    /// <c>uploadUrl = null</c> (contrato <c>images-api.md</c>). <c>pending</c> ganha URL sempre nova,
    /// porque a anterior pode ter expirado sem nunca ter sido usada.
    /// </summary>
    private ImageUploadResult BuildUploadResult(StoredImage image, string mimeType, DateTime now)
    {
        if (image.Status == ImageStatus.Available)
        {
            return new ImageUploadResult
            {
                ImageId = image.Id,
                AlreadyExists = true,
                Image = MapToInfo(image)
            };
        }

        var upload = _storage.GetPresignedUploadUrl(image.StorageKey, mimeType, now);

        return new ImageUploadResult
        {
            ImageId = image.Id,
            AlreadyExists = false,
            UploadUrl = upload.Url,
            UploadMethod = "PUT",
            ContentType = mimeType,
            UploadExpiresAt = upload.ExpiresAt
        };
    }

    /// <summary>
    /// Efeito colateral de uma recusa por conteúdo (research §6, FR-007): apaga o objeto <b>primeiro</b>,
    /// marca <c>failed</c> e só então devolve a exceção — assim não sobra binário recusado sem registro
    /// que o aponte, e a chave fica livre para um reenvio do mesmo conteúdo (clarificação 5 da spec).
    /// </summary>
    private async Task<ImageRejectedException> RejectAsync(StoredImage image, ImageRejectionReason reason, DateTime now)
    {
        await _storage.DeleteAsync(image.StorageKey);

        image.MarkFailed(now);
        await _images.UpdateAsync(image);

        _logger.LogWarning("Envio da imagem {ImageId} recusado ({Reason}).", image.Id, reason);

        return new ImageRejectedException(reason);
    }

    /// <summary>
    /// Grava o binário no storage com o <c>Content-Type</c> já conferido contra os tipos aceitos.
    /// </summary>
    private async Task PutContentAsync(string key, byte[] content, string mimeType)
    {
        using var stream = new MemoryStream(content, writable: false);

        await _storage.PutAsync(key, stream, mimeType);
    }

    /// <summary>
    /// Copia no máximo <c>MaxUploadBytes + 1</c> bytes e devolve <c>null</c> se estourar: aborta sem
    /// descargar um arquivo gigante inteiro (research §6). O limite é o da configuração, não o do
    /// cabeçalho — o cabeçalho pode mentir.
    /// </summary>
    private async Task<MemoryStream?> CopyBoundedAsync(Stream source)
    {
        var buffer = new MemoryStream();
        var chunk = new byte[CopyBufferSize];
        var total = 0L;

        while (true)
        {
            var read = await source.ReadAsync(chunk);

            if (read == 0)
            {
                buffer.Position = 0;
                return buffer;
            }

            total += read;

            if (total > _settings.MaxUploadBytes)
            {
                buffer.Dispose();
                return null;
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read));
        }
    }

    private static ImageRejectedException EmptyOrTooLarge() => new(ImageRejectionReason.Size);

    private bool IsAllowedMimeType(string mimeType) =>
        _settings.AllowedMimeTypes.Any(allowed => string.Equals(allowed, mimeType, StringComparison.OrdinalIgnoreCase));

    private async Task ThrowIfInvalidAsync(ImageUploadInsertInfo info)
    {
        var result = await _uploadValidator.ValidateAsync(info);

        if (!result.IsValid)
        {
            throw new ValidationException(result.Errors);
        }
    }

    /// <summary>
    /// Normalização compartilhada: hash em hex minúsculo e tipo em minúsculas, porque a chave do objeto
    /// e o índice único comparam byte a byte (data-model).
    /// </summary>
    private static string NormalizeSha256(string sha256) => sha256.Trim().ToLowerInvariant();

    private static string NormalizeMimeType(string mimeType) => mimeType.Trim().ToLowerInvariant();

    /// <summary>SHA-256 em hex minúsculo — o formato da coluna <c>sha256 varchar(64)</c>.</summary>
    private static string Sha256Hex(ReadOnlySpan<byte> data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
}
