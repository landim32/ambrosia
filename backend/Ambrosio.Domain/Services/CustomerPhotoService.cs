using Ambrosio.Domain.Exceptions;
using Ambrosio.Domain.Interfaces;
using Ambrosio.Domain.Models;
using Ambrosio.DTO.CustomerPhotos;
using Ambrosio.DTO.Enums;
using Ambrosio.Infra.Interfaces.AppServices;
using Ambrosio.Infra.Interfaces.Repository;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Logging;

namespace Ambrosio.Domain.Services;

/// <summary>
/// Regras do álbum de fotos da cliente (feature 009). Fala com o storage apenas pelas abstrações da
/// Infra, igual ao <see cref="ImageService"/> da 004, e <b>jamais</b> decodifica um arquivo: o que
/// este serviço conhece de uma foto é o metadado dela (data-model §3, research R2).
/// <para>
/// As três portas de entrada — tela, API REST e conversa no ChatGPT — passam por aqui, então uma regra
/// escrita uma vez vale nos três canais (FR-023, SC-007).
/// </para>
/// <para>
/// O log menciona no máximo <c>photoId</c>, <c>customerId</c>, <c>imageId</c> e <c>userId</c>: chave de
/// objeto e URL assinada ficam de fora (FR-026).
/// </para>
/// </summary>
public class CustomerPhotoService : ICustomerPhotoService
{
    /// <summary>
    /// Mensagem única de ausência de cliente — "não existe" e "é de outra vendedora" (SC-006). É a
    /// mesma frase que a API devolve desde a feature 001/007 (<c>CustomerService</c> e
    /// <c>InsightService</c>): um 404 por cliente não pode ter duas redações na mesma API, porque
    /// cliente que casa a mensagem por texto quebra na primeira.
    /// </summary>
    private const string CustomerNotFound = "Cliente não encontrado.";

    /// <summary>Mensagem única de ausência de foto, pelo mesmo motivo de <see cref="CustomerNotFound"/>.</summary>
    private const string PhotoNotFound = "Foto não encontrada.";

    /// <summary>A imagem existe, mas ainda não é legível (pendente de envio ou recusada pela 004).</summary>
    private const string ImageNotAvailable = "A imagem ainda não está disponível.";

    private readonly ICustomerPhotoRepository<CustomerPhoto> _photos;
    private readonly ICustomerRepository<Customer> _customers;
    private readonly IStoredImageRepository<StoredImage> _images;
    private readonly IImageService _imageService;
    private readonly IObjectStorageAppService _storage;
    private readonly IValidator<CustomerPhotoListQuery> _listValidator;
    private readonly IValidator<CustomerPhotoInsertInfo> _insertValidator;
    private readonly IValidator<CustomerPhotoDescriptionInfo> _descriptionValidator;
    private readonly IValidator<ProfilePhotoUpdateInfo> _profileValidator;
    private readonly IClock _clock;
    private readonly ILogger<CustomerPhotoService> _logger;

    public CustomerPhotoService(
        ICustomerPhotoRepository<CustomerPhoto> photos,
        ICustomerRepository<Customer> customers,
        IStoredImageRepository<StoredImage> images,
        IImageService imageService,
        IObjectStorageAppService storage,
        IValidator<CustomerPhotoListQuery> listValidator,
        IValidator<CustomerPhotoInsertInfo> insertValidator,
        IValidator<CustomerPhotoDescriptionInfo> descriptionValidator,
        IValidator<ProfilePhotoUpdateInfo> profileValidator,
        IClock clock,
        ILogger<CustomerPhotoService> logger)
    {
        _photos = photos;
        _customers = customers;
        _images = images;
        _imageService = imageService;
        _storage = storage;
        _listValidator = listValidator;
        _insertValidator = insertValidator;
        _descriptionValidator = descriptionValidator;
        _profileValidator = profileValidator;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<CustomerPhotoPage> ListAsync(
        long userId, long customerId, CustomerPhotoListQuery query)
    {
        await ThrowIfInvalidAsync(_listValidator, query);

        var customer = await RequireCustomerAsync(userId, customerId);

        // Lê um a mais para saber se existe próxima página, sem contar a tabela (research R8).
        var fetched = await _photos.ListAsync(
            userId, customerId, query.Cursor, query.PageSize + 1, query.Favorites);

        var hasMore = fetched.Count > query.PageSize;
        var items = hasMore ? fetched.Take(query.PageSize).ToList() : fetched;

        return new CustomerPhotoPage
        {
            Items = items.Select(photo => Map(photo, customer)).ToList(),
            NextCursor = hasMore && items.Count > 0 ? items[^1].Id : null
        };
    }

    /// <inheritdoc />
    public async Task<CustomerPhotoInsertResult> AttachAsync(
        long userId, long customerId, CustomerPhotoInsertInfo info, CustomerPhotoSource? canal = null)
    {
        await ThrowIfInvalidAsync(_insertValidator, info);

        var customer = await RequireCustomerAsync(userId, customerId);
        var image = await RequireAvailableImageAsync(userId, info.ImageId);

        // O canal dito por quem chama (a conversa) tem precedência sobre o campo do corpo, que o
        // validator mantém limitado a web/api — é assim que a foto do ChatGpt chega a ser registrada
        // como `chatGpt` sem que uma integração possa forjar a origem no JSON (research R12, FR-002).
        var source = canal ?? info.Source ?? CustomerPhotoSource.Api;

        // Caminho feliz da deduplicação (FR-006): a foto já está no álbum desta cliente. Devolve a
        // existente sem tocar em nada — inclusive na descrição, que é da foto, não do reenvio.
        var existing = await _photos.FindByImageAsync(userId, customerId, image.Id);

        if (existing is not null)
        {
            // Sem checar as outras clientes aqui: é o caso em que nada mudou, e a leitura extra não
            // paga um aviso que o chamador já viu da primeira vez.
            return new CustomerPhotoInsertResult
            {
                Photo = Map(await RequirePhotoAsync(userId, customerId, existing.Id), customer),
                Created = false
            };
        }

        CustomerPhoto photo;

        try
        {
            photo = await _photos.InsertAsync(CustomerPhoto.Create(
                userId, customerId, image.Id, info.Description, source, _clock.UtcNow));
        }
        catch (DuplicateException)
        {
            // Corrida do arraste em lote: duas requisições do mesmo arquivo chegaram juntas e o
            // índice único decidiu o vencedor (research R6). O perdedor relê e atende igual.
            var winner = await _photos.FindByImageAsync(userId, customerId, image.Id)
                ?? throw new NotFoundException(PhotoNotFound);

            return new CustomerPhotoInsertResult
            {
                Photo = Map(await RequirePhotoAsync(userId, customerId, winner.Id), customer),
                Created = false
            };
        }

        // Informativo: a mesma imagem pode estar no álbum de outra cliente da mesma vendedora
        // (edge case da spec), e a conversa usa isto para avisar sem esconder nada (research R6).
        var alsoInOtherCustomers = await _photos.ExistsInOtherCustomerAsync(userId, customerId, image.Id);

        _logger.LogInformation(
            "Foto {PhotoId} anexada à cliente {CustomerId} (imagem {ImageId}) pelo usuário {UserId}.",
            photo.Id, customerId, image.Id, userId);

        // Relida: a entidade recém-inserida não carrega a navegação da imagem, e a grade precisa da
        // url já nesta resposta (contracts/api.md §2) — sem ela a célula ficaria em branco até a
        // próxima listagem.
        return new CustomerPhotoInsertResult
        {
            Photo = Map(await RequirePhotoAsync(userId, customerId, photo.Id), customer),
            Created = true,
            AlsoInOtherCustomers = alsoInOtherCustomers
        };
    }

    /// <inheritdoc />
    public async Task<CustomerPhotoInfo> UpdateDescriptionAsync(
        long userId, long customerId, long photoId, CustomerPhotoDescriptionInfo info)
    {
        await ThrowIfInvalidAsync(_descriptionValidator, info);

        var customer = await RequireCustomerAsync(userId, customerId);
        var photo = await RequirePhotoAsync(userId, customerId, photoId);

        photo.UpdateDescription(info.Description, _clock.UtcNow);

        await _photos.UpdateAsync(photo);

        // Relida com a navegação da imagem: UpdateAsync devolve a entidade rastreada, que não traz
        // StoredImage, e a resposta do contrato precisa sair com url e urlExpiresAt preenchidos.
        return Map(await RequirePhotoAsync(userId, customerId, photoId), customer);
    }

    /// <inheritdoc />
    public async Task<CustomerPhotoInfo> SetFavoriteAsync(
        long userId, long customerId, long photoId, bool favorite)
    {
        var customer = await RequireCustomerAsync(userId, customerId);
        var photo = await RequirePhotoAsync(userId, customerId, photoId);

        // Idempotente por construção: o modelo não mexe em updated_at quando o valor já é o mesmo,
        // então a conversa pode repetir o pedido sem embaralhar a "última alteração" (FR-008).
        photo.SetFavorite(favorite, _clock.UtcNow);

        await _photos.UpdateAsync(photo);

        _logger.LogInformation(
            "Foto {PhotoId} {Action} como favorita pelo usuário {UserId}.",
            photo.Id, favorite ? "marcada" : "desmarcada", userId);

        // Relida pelo mesmo motivo de UpdateDescriptionAsync: sem a navegação não há url na resposta.
        return Map(await RequirePhotoAsync(userId, customerId, photoId), customer);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(long userId, long customerId, long photoId)
    {
        var photo = await RequirePhotoAsync(userId, customerId, photoId);

        // O recorte de perfil, se havia, só é conhecido depois da limpeza — o repositório devolve o id
        // porque foi ele que viu o perfil apontando para esta foto (FR-015).
        var orphanedCropImageId = await _photos.DeleteWithProfileClearAsync(userId, photo.Id, _clock.UtcNow);

        // Ordem deliberada: o banco primeiro, o arquivo depois. Um objeto que sobra sem metadado é
        // invisível para a vendedora e inofensivo; um metadado apontando para um objeto apagado é um
        // avatar quebrado para sempre (mesma razão de ser da FR-016 da 004, invertida aqui porque o
        // metadado desta vez já foi removido com segurança transacional).
        await DeleteIfOrphanAsync(userId, photo.ImageId);

        if (orphanedCropImageId is { } cropId)
        {
            await DeleteIfOrphanAsync(userId, cropId);
        }

        _logger.LogInformation(
            "Foto {PhotoId} removida do álbum da cliente {CustomerId} pelo usuário {UserId}.",
            photoId, customerId, userId);
    }

    /// <inheritdoc />
    public async Task<CustomerProfilePhotoInfo> SetProfileAsync(
        long userId, long customerId, ProfilePhotoUpdateInfo info)
    {
        await ThrowIfInvalidAsync(_profileValidator, info);

        var customer = await RequireCustomerAsync(userId, customerId);

        // A foto precisa ser desta cliente: `profile_photo_id` é FK e o banco não sabe que o álbum é
        // de outra cliente (data-model §2). Foto de outra cliente responde como ausente.
        var photo = await RequirePhotoAsync(userId, customerId, info.PhotoId);

        long? cropImageId = null;

        if (info.ImageId is { } requestedCropId)
        {
            var crop = await RequireAvailableImageAsync(userId, requestedCropId);
            cropImageId = crop.Id;
        }

        var previousCropImageId = customer.SetProfilePhoto(photo.Id, cropImageId, _clock.UtcNow);

        await _customers.UpdateAsync(customer);

        if (previousCropImageId is { } oldCropId && oldCropId != cropImageId)
        {
            await DeleteIfOrphanAsync(userId, oldCropId);
        }

        _logger.LogInformation(
            "Foto de perfil da cliente {CustomerId} definida pela foto {PhotoId} pelo usuário {UserId}.",
            customerId, photo.Id, userId);

        return await MapProfileAsync(customer, photo);
    }

    /// <inheritdoc />
    public async Task RemoveProfileAsync(long userId, long customerId)
    {
        var customer = await RequireCustomerAsync(userId, customerId);

        var previousCropImageId = customer.ClearProfilePhoto(_clock.UtcNow);

        await _customers.UpdateAsync(customer);

        // A foto de onde o perfil saiu continua no álbum (FR-015); só o recorte dedicado sai, se ficou
        // sem dono.
        if (previousCropImageId is { } cropId)
        {
            await DeleteIfOrphanAsync(userId, cropId);
        }

        _logger.LogInformation(
            "Foto de perfil da cliente {CustomerId} removida pelo usuário {UserId}.",
            customerId, userId);
    }

    /// <inheritdoc />
    public async Task<int> CountAsync(long userId, long customerId)
    {
        await RequireCustomerAsync(userId, customerId);

        return await _photos.CountForCustomerAsync(userId, customerId);
    }

    /// <inheritdoc />
    public async Task<Dictionary<long, CustomerProfilePhotoInfo>> GetProfilesAsync(
        long userId, IReadOnlyCollection<long> customerIds, bool includeOriginalUrl)
    {
        var profiles = await _photos.ListProfilesAsync(userId, customerIds);

        if (profiles.Count == 0)
        {
            return [];
        }

        // Uma única leitura para todas as imagens envolvidas — o recorte e o original de cada cliente —
        // porque assinar URL exige a chave do objeto, e buscá-la linha a linha seria um N+1 em toda
        // listagem de clientes (T042).
        var imageIds = profiles
            .SelectMany(p => new[] { p.CropImageId ?? p.AlbumImageId, p.AlbumImageId })
            .Distinct()
            .ToList();

        var images = (await _images.GetByIdsAsync(userId, imageIds))
            .ToDictionary(i => i.Id);

        var now = _clock.UtcNow;
        var result = new Dictionary<long, CustomerProfilePhotoInfo>(profiles.Count);

        foreach (var profile in profiles)
        {
            var displayId = profile.CropImageId ?? profile.AlbumImageId;

            if (!images.TryGetValue(displayId, out var displayImage))
            {
                continue;
            }

            var info = new CustomerProfilePhotoInfo
            {
                PhotoId = profile.PhotoId,
                ImageId = displayId,
                OriginalImageId = profile.AlbumImageId
            };

            if (displayImage.Status == ImageStatus.Available)
            {
                var url = _storage.GetPresignedReadUrl(displayImage.StorageKey, now);

                info.Url = url.Url;
                info.UrlExpiresAt = url.ExpiresAt;

                // Na listagem isso fica de fora: uma segunda assinatura por linha não paga nada, já
                // que a lista não abre visualizador (data-model §6).
                if (includeOriginalUrl
                    && profile.AlbumImageId != displayId
                    && images.TryGetValue(profile.AlbumImageId, out var originalImage)
                    && originalImage.Status == ImageStatus.Available)
                {
                    info.OriginalUrl = _storage.GetPresignedReadUrl(originalImage.StorageKey, now).Url;
                }
            }

            result[profile.CustomerId] = info;
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<int> DeleteAllForCustomerAsync(long userId, long customerId)
    {
        await RequireCustomerAsync(userId, customerId);

        var removed = await _photos.CountForCustomerAsync(userId, customerId);

        if (removed == 0)
        {
            return 0;
        }

        // Ids antes de apagar: depois da linha sumir, não há como saber que imagem checar como órfã.
        var imageIds = await _photos.ListImageIdsForCustomerAsync(userId, customerId);

        await _photos.DeleteAllForCustomerAsync(userId, customerId);

        foreach (var imageId in imageIds)
        {
            await DeleteIfOrphanAsync(userId, imageId);
        }

        _logger.LogInformation(
            "Álbum da cliente {CustomerId} removido: {Count} fotos pelo usuário {UserId}.",
            customerId, removed, userId);

        return removed;
    }

    /// <summary>
    /// Apaga a imagem do banco e do storage <b>só se ninguém mais a usa</b> (research R6). O uso pode
    /// vir de outra foto do álbum (inclusive de outra cliente), do recorte de perfil de alguma cliente,
    /// ou de um item de pedido importado — e sumir com a imagem de um pedido porque a vendedora apagou
    /// uma foto seria estrago cruzado.
    /// </summary>
    private async Task DeleteIfOrphanAsync(long userId, long imageId)
    {
        if (await _images.IsImageReferencedAsync(imageId, userId))
        {
            return;
        }

        try
        {
            // Reuso da 004 de propósito: ela é a dona da ordem "objeto primeiro, metadado depois" e do
            // cuidado com chave determinística (FR-016). reimplementar aqui seria ter duas regras.
            await _imageService.DeleteAsync(userId, imageId);
        }
        catch (NotFoundException)
        {
            // O job de limpeza da 004 pode ter recolhido a imagem entre a nossa leitura e agora.
            // Não é erro: o objetivo (não sobrar arquivo) já está alcançado.
        }
    }

    /// <summary>Cliente do usuário, ou 404 com a mensagem única (FR-025).</summary>
    private async Task<Customer> RequireCustomerAsync(long userId, long customerId) =>
        await _customers.GetByIdAsync(customerId, userId)
        ?? throw new NotFoundException(CustomerNotFound);

    /// <summary>Foto do álbum daquela cliente, ou 404 com a mensagem única.</summary>
    private async Task<CustomerPhoto> RequirePhotoAsync(long userId, long customerId, long photoId) =>
        await _photos.GetByIdAsync(userId, customerId, photoId)
        ?? throw new NotFoundException(PhotoNotFound);

    /// <summary>
    /// Imagem do usuário em situação <c>available</c>. Pendente ou recusada é 400 citando
    /// <c>imageId</c>: o arquivo ainda nem está no storage, e isso não é "não encontrado".
    /// </summary>
    private async Task<StoredImage> RequireAvailableImageAsync(long userId, long imageId)
    {
        var image = await _images.GetByIdAsync(imageId, userId)
            ?? throw new NotFoundException("Imagem não encontrada.");

        if (image.Status != ImageStatus.Available)
        {
            throw new ValidationException([new ValidationFailure(nameof(imageId), ImageNotAvailable)]);
        }

        return image;
    }

    /// <summary>
    /// Metadado da foto → DTO, com a URL assinada <b>a cada resposta</b> e nunca persistida
    /// (FR-012/FR-026 da 004, que as fotos herdam). A grade recebe o arquivo original: não existe
    /// segunda imagem por foto nesta feature (research R3).
    /// </summary>
    private CustomerPhotoInfo Map(CustomerPhoto photo, Customer customer)
    {
        var info = new CustomerPhotoInfo
        {
            PhotoId = photo.Id,
            CustomerId = photo.CustomerId,
            ImageId = photo.ImageId,
            Description = photo.Description,
            IsFavorite = photo.IsFavorite,
            IsProfile = customer.ProfilePhotoId == photo.Id,
            Source = photo.Source,
            CreatedAt = photo.CreatedAt,
            UpdatedAt = photo.UpdatedAt
        };

        var image = photo.StoredImage;

        if (image is not null && image.Status == ImageStatus.Available)
        {
            var url = _storage.GetPresignedReadUrl(image.StorageKey, _clock.UtcNow);

            info.Url = url.Url;
            info.UrlExpiresAt = url.ExpiresAt;
        }

        return info;
    }

    private async Task<CustomerProfilePhotoInfo> MapProfileAsync(Customer customer, CustomerPhoto photo)
    {
        var displayImageId = customer.ProfileImageId ?? photo.ImageId;

        var info = new CustomerProfilePhotoInfo
        {
            PhotoId = photo.Id,
            ImageId = displayImageId,
            OriginalImageId = photo.ImageId
        };

        // A imagem exibida no círculo pode ser o recorte (outra linha de stored_images) ou o arquivo
        // da foto: assina-se a que for, e o original só quando é outra.
        var images = await _images.GetByIdsAsync(
            customer.UserId, [displayImageId, photo.ImageId]);

        var byId = images.ToDictionary(i => i.Id);
        var now = _clock.UtcNow;

        if (byId.TryGetValue(displayImageId, out var display) && display.Status == ImageStatus.Available)
        {
            var url = _storage.GetPresignedReadUrl(display.StorageKey, now);

            info.Url = url.Url;
            info.UrlExpiresAt = url.ExpiresAt;
        }

        if (photo.ImageId != displayImageId
            && byId.TryGetValue(photo.ImageId, out var original)
            && original.Status == ImageStatus.Available)
        {
            info.OriginalUrl = _storage.GetPresignedReadUrl(original.StorageKey, now).Url;
        }

        return info;
    }

    private static async Task ThrowIfInvalidAsync<T>(IValidator<T> validator, T instance)
    {
        var result = await validator.ValidateAsync(instance);

        if (!result.IsValid)
        {
            throw new ValidationException(result.Errors);
        }
    }
}
