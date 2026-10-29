using System.Text.Json;
using Ambrosio.ApiTests.Fixtures;
using Ambrosio.ApiTests.Helpers;
using Ambrosio.DTO.Common;
using Ambrosio.DTO.CustomerPhotos;
using Ambrosio.DTO.Customers;
using Ambrosio.DTO.Enums;
using Ambrosio.DTO.Images;
using Ambrosio.DTO.Orders;
using FluentAssertions;
using Xunit.Abstractions;

namespace Ambrosio.ApiTests.Controllers;

/// <summary>
/// Testes de API do álbum de fotos da cliente (feature 009, skill <c>dotnet-test-api</c>), contra a
/// <c>ApiBaseUrl</c> do ambiente em teste. Esta classe cobre a fundação: anexar e listar
/// (contracts/api.md §1 e §2).
/// <para>
/// <b>Nenhum byte de imagem é gerado aqui, e nenhum teste asserta sobre conteúdo decodificado.</b> O
/// Ambrosio não decodifica foto de cliente — os fixtures versionados sobem pela rota da 004 e o que se
/// verifica é o relacionamento <c>imageId</c> ↔ cliente, a deduplicação e as respostas de erro
/// (research R2). Asasserções de EXIF, orientação, miniatura e megapixel que existiam no plano
/// anterior saíram junto com o decodificador.
/// </para>
/// <para>
/// Cada teste anexa um arquivo <b>próprio</b> (<see cref="TestDataHelper.UniquePhotoBytes"/>): a
/// deduplicação é por <c>(usuário, sha256)</c>, e bytes fixos fariam o segundo teste receber a foto do
/// primeiro e passar pelo motivo errado.
/// </para>
/// </summary>
[Collection(ApiTestCollection.API_TESTS)]
public class CustomerPhotosControllerTests
{
    private readonly ApiTestFixture _fixture;
    private readonly ITestOutputHelper _output;

    public CustomerPhotosControllerTests(ApiTestFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    private string Token => _fixture.TokenA;

    // ---------------------------------------------------------------------
    // Anexar foto (US2/US6 — contracts/api.md §2)
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Attach_AvailableImage_ShouldReturn201WithPhotoAndSignedUrl()
    {
        // Arrange: US6 cenário 1 — um cliente dele e uma imagem já gravada pela 004.
        var customer = await _fixture.CreateCustomerAsync(Token);
        var image = await _fixture.UploadImageAsync(Token);

        // Act
        var response = await _fixture.AttachPhotoRawAsync(Token, customer.Id, image.ImageId);

        // Assert
        response.StatusCode.Should().Be(201, $"anexar foto deveria funcionar: {response}");

        var result = response.As<CustomerPhotoInsertResult>();

        response.Location.Should().Be($"/customers/{customer.Id}/photos/{result.Photo.PhotoId}");
        result.Created.Should().BeTrue();
        result.Photo.PhotoId.Should().BeGreaterThan(0);
        result.Photo.CustomerId.Should().Be(customer.Id);
        result.Photo.ImageId.Should().Be(image.ImageId);
        result.Photo.Description.Should().BeNull("o arraste entra sem descrição (FR-020)");
        result.Photo.IsFavorite.Should().BeFalse();
        result.Photo.IsProfile.Should().BeFalse();
        result.Photo.Source.Should().Be(CustomerPhotoSource.Api,
            "corpo sem origem declarada é integração pela API (research R12)");
        result.Photo.Url.Should().NotBeNullOrWhiteSpace("a grade mostra o original, e ele precisa de URL (research R3)");
        result.Photo.UrlExpiresAt.Should().BeAfter(DateTime.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task Attach_WebPFile_ShouldWorkLikeAnyOtherAcceptedType()
    {
        // A rota de foto não conhece formato: ela só aponta para uma imagem que a 004 aceitou
        // (research R2). WebP é o caso que prova que não há suposição de JPEG em lugar nenhum.
        var customer = await _fixture.CreateCustomerAsync(Token);
        var image = await _fixture.UploadImageAsync(Token, "small.webp", "image/webp");

        var response = await _fixture.AttachPhotoRawAsync(Token, customer.Id, image.ImageId);

        response.StatusCode.Should().Be(201, $"anexar WebP deveria funcionar: {response}");

        var result = response.As<CustomerPhotoInsertResult>();

        result.Photo.ImageId.Should().Be(image.ImageId);
        result.Photo.Url.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Attach_DeclaredTypeNotInTheAllowedList_ShouldReturn400FromTheImageRoute()
    {
        // FR-003 na redação revista: a recusa de tipo acontece na rota de imagem da 004, pelo tipo
        // DECLARADO — não há sniffing de conteúdo (a checagem de assinatura foi recusada pelo usuário).
        var customer = await _fixture.CreateCustomerAsync(Token);

        var upload = await _fixture.PostFileAsync(
            "/images", Token, TestDataHelper.UniquePhotoBytes("small.jpg"), "image/gif", "foto.gif");

        upload.StatusCode.Should().Be(400, $"tipo fora da lista deveria ser recusado: {upload}");

        var body = upload.As<ErrorResult>();

        body.Sucesso.Should().BeFalse();
        body.Mensagem.Should().NotBeNullOrWhiteSpace();

        // Sem imagem gravada, não há o que anexar: a rota de fotos nem é chamada com sucesso aqui.
        var list = await _fixture.GetAsync(TestDataHelper.PhotosPath(customer.Id), Token);

        list.As<CustomerPhotoPage>().Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Attach_SameImageTwice_ShouldReturn200AndKeepTheOriginalPhoto()
    {
        // FR-006 e cenário US2-5: repetir a imagem não cria segunda foto nem sobrescreve a legenda.
        var customer = await _fixture.CreateCustomerAsync(Token);
        var image = await _fixture.UploadImageAsync(Token);

        var first = await _fixture.AttachPhotoRawAsync(Token, customer.Id, image.ImageId, "provando o vestido azul");
        var second = await _fixture.AttachPhotoRawAsync(Token, customer.Id, image.ImageId, "outra legenda");

        first.StatusCode.Should().Be(201);
        second.StatusCode.Should().Be(200, $"repetição não cria foto: {second}");

        var firstPhoto = first.As<CustomerPhotoInsertResult>().Photo;
        var secondResult = second.As<CustomerPhotoInsertResult>();

        secondResult.Created.Should().BeFalse();
        secondResult.Photo.PhotoId.Should().Be(firstPhoto.PhotoId);
        secondResult.Photo.Description.Should().Be("provando o vestido azul",
            "a legenda é da foto existente, não do reenvio");

        var page = (await _fixture.GetAsync(TestDataHelper.PhotosPath(customer.Id), Token))
            .As<CustomerPhotoPage>();

        page.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task Attach_SameImageForAnotherCustomer_ShouldFlagAlsoInOtherCustomers()
    {
        // Edge case "mesma foto em duas clientes": a mesma imagem, duas linhas, e o aviso na resposta.
        var ana = await _fixture.CreateCustomerAsync(Token);
        var bruna = await _fixture.CreateCustomerAsync(Token);
        var image = await _fixture.UploadImageAsync(Token);

        await _fixture.AttachPhotoRawAsync(Token, ana.Id, image.ImageId);

        var second = await _fixture.AttachPhotoRawAsync(Token, bruna.Id, image.ImageId);

        second.StatusCode.Should().Be(201, $"outra cliente é outra foto: {second}");

        var result = second.As<CustomerPhotoInsertResult>();

        result.Created.Should().BeTrue();
        result.AlsoInOtherCustomers.Should().BeTrue("a imagem já estava no álbum da outra cliente");
    }

    [Fact]
    public async Task Attach_UnknownImage_ShouldReturn404WithNotFoundMessage()
    {
        // "Não existe" e "é de outra vendedora" respondem igual (FR-025, SC-006).
        var customer = await _fixture.CreateCustomerAsync(Token);

        var response = await _fixture.AttachPhotoRawAsync(Token, customer.Id, 999_999_999);

        response.StatusCode.Should().Be(404, $"imagem inexistente deveria dar 404: {response}");
        response.As<ErrorResult>().Mensagem.Should().Be("Imagem não encontrada.");
    }

    [Fact]
    public async Task Attach_ImageOfAnotherSeller_ShouldReturn404_AndNotTouchTheOtherAlbum()
    {
        // A vendedora B anexa ao cliente DELA a imagem da vendedora A: 404, sem revelar que existe.
        var imageOfA = await _fixture.UploadImageAsync(Token);
        var (tokenB, _) = await _fixture.NewSellerAsync("Vendedora B");
        var customerOfB = await _fixture.CreateCustomerAsync(tokenB);

        var response = await _fixture.AttachPhotoRawAsync(tokenB, customerOfB.Id, imageOfA.ImageId);

        response.StatusCode.Should().Be(404, $"imagem de outra conta não deveria ser visível: {response}");

        var page = (await _fixture.GetAsync(TestDataHelper.PhotosPath(customerOfB.Id), tokenB))
            .As<CustomerPhotoPage>();

        page.Items.Should().BeEmpty("a foto não pode ter entrado por cima do isolamento");
    }

    [Fact]
    public async Task Attach_CustomerOfAnotherSeller_ShouldReturn404()
    {
        var customerOfA = await _fixture.CreateCustomerAsync(Token);
        var (tokenB, _) = await _fixture.NewSellerAsync("Vendedora B");
        var imageOfB = await _fixture.UploadImageAsync(tokenB);

        var response = await _fixture.AttachPhotoRawAsync(tokenB, customerOfA.Id, imageOfB.ImageId);

        response.StatusCode.Should().Be(404, $"cliente de outra conta não deveria ser visível: {response}");
        response.As<ErrorResult>().Mensagem.Should().Be("Cliente não encontrado.");
    }

    [Fact]
    public async Task Attach_PendingImage_ShouldReturn400MentioningImageId()
    {
        // A imagem foi declarada mas nunca confirmada: não está no storage, e a grade não teria o que
        // mostrar (contracts/api.md §10).
        var customer = await _fixture.CreateCustomerAsync(Token);
        var pendingId = await _fixture.DeclarePendingImageAsync(Token);

        var response = await _fixture.AttachPhotoRawAsync(Token, customer.Id, pendingId);

        response.StatusCode.Should().Be(400, $"imagem pendente não pode virar foto: {response}");

        var body = response.As<ErrorResult>();

        body.Mensagem.Should().Be("Dados inválidos.");
        body.Erros.Should().ContainSingle()
            .Which.Campo.Should().Be("imageId", "o contrato cita o campo (constituição §IV)");
    }

    [Fact]
    public async Task Attach_SourceChatGpt_ShouldReturn400()
    {
        // A conversa tem origem própria (a tool); pela rota REST ela é recusada, para uma integração
        // não se passar pelo ChatGPT (research R12).
        var customer = await _fixture.CreateCustomerAsync(Token);
        var image = await _fixture.UploadImageAsync(Token);

        var response = await _fixture.AttachPhotoRawAsync(
            Token, customer.Id, image.ImageId, source: "chatGpt");

        response.StatusCode.Should().Be(400, $"origem da conversa não é aceita na rota: {response}");
        response.As<ErrorResult>().Erros.Should().ContainSingle()
            .Which.Campo.Should().Be("source");
    }

    [Fact]
    public async Task Attach_DescriptionAboveFiveHundred_ShouldReturn400WithField()
    {
        var customer = await _fixture.CreateCustomerAsync(Token);
        var image = await _fixture.UploadImageAsync(Token);

        var response = await _fixture.AttachPhotoRawAsync(
            Token, customer.Id, image.ImageId, new string('a', 501));

        response.StatusCode.Should().Be(400, $"descrição gigante deveria ser recusada: {response}");

        var error = response.As<ErrorResult>().Erros.Should().ContainSingle().Which;

        error.Campo.Should().Be("description");
        error.Mensagem.Should().Be("A descrição deve ter no máximo 500 caracteres.");
    }

    // ---------------------------------------------------------------------
    // Listar o álbum (US1/US6 — contracts/api.md §1)
    // ---------------------------------------------------------------------

    [Fact]
    public async Task List_EmptyAlbum_ShouldReturnEmptyItemsAndNullCursor()
    {
        // US1 cenário 6: cliente sem fotos tem aba aberta e vazia, não erro.
        var customer = await _fixture.CreateCustomerAsync(Token);

        var response = await _fixture.GetAsync(TestDataHelper.PhotosPath(customer.Id), Token);

        response.StatusCode.Should().Be(200);

        var page = response.As<CustomerPhotoPage>();

        page.Items.Should().BeEmpty();
        page.NextCursor.Should().BeNull();
    }

    [Fact]
    public async Task List_ThreePhotos_ShouldReturnMostRecentFirstAndNoThumbnailField()
    {
        // US1 cenário 1 + research R3: ordem da mais recente, e a grade lê o ORIGINAL — não existe
        // miniatura, então a presença de "thumbnailUrl" na resposta seria a feature mentindo no contrato.
        var customer = await _fixture.CreateCustomerAsync(Token);

        var first = await _fixture.AttachPhotoAsync(Token, customer.Id);
        var second = await _fixture.AttachPhotoAsync(Token, customer.Id, "other.jpg");
        var third = await _fixture.AttachPhotoAsync(Token, customer.Id, "small.png");

        var response = await _fixture.GetAsync(TestDataHelper.PhotosPath(customer.Id), Token);

        response.StatusCode.Should().Be(200);

        var page = response.As<CustomerPhotoPage>();

        page.Items.Select(i => i.PhotoId).Should().Equal(
            third.Photo.PhotoId, second.Photo.PhotoId, first.Photo.PhotoId);

        page.NextCursor.Should().BeNull("três fotos cabem na página padrão de 24");

        using var json = JsonDocument.Parse(response.Body);
        var item = json.RootElement.GetProperty("items")[0];

        item.TryGetProperty("thumbnailUrl", out _).Should().BeFalse(
            "não há miniatura gerada no servidor (research R3)");
        item.GetProperty("url").GetString().Should().NotBeNullOrWhiteSpace();
        response.Body.Should().NotContain("storageKey", "a chave do objeto nunca sai na resposta (FR-026)");
    }

    [Fact]
    public async Task List_PagingByCursor_ShouldNotRepeatNorSkipPhotos()
    {
        // Research R8: com offset, uma foto nova no meio da rolagem empurra itens e duplica células.
        var customer = await _fixture.CreateCustomerAsync(Token);

        for (var i = 0; i < 5; i++)
        {
            await _fixture.AttachPhotoAsync(Token, customer.Id);
        }

        var page1 = (await _fixture.GetAsync(
            $"{TestDataHelper.PhotosPath(customer.Id)}?pageSize=2", Token)).As<CustomerPhotoPage>();

        page1.Items.Should().HaveCount(2);
        page1.NextCursor.Should().NotBeNull("há mais fotos");

        var page2 = (await _fixture.GetAsync(
            $"{TestDataHelper.PhotosPath(customer.Id)}?pageSize=2&cursor={page1.NextCursor}", Token))
            .As<CustomerPhotoPage>();

        page2.Items.Should().HaveCount(2);
        page2.Items.Select(i => i.PhotoId).Intersect(page1.Items.Select(i => i.PhotoId))
            .Should().BeEmpty("o cursor não pode repetir foto");

        var page3 = (await _fixture.GetAsync(
            $"{TestDataHelper.PhotosPath(customer.Id)}?pageSize=2&cursor={page2.NextCursor}", Token))
            .As<CustomerPhotoPage>();

        page3.Items.Should().HaveCount(1, "a quinta foto é a última");
        page3.NextCursor.Should().BeNull("fim da lista");

        page1.Items.Concat(page2.Items).Concat(page3.Items).Select(i => i.PhotoId).Distinct()
            .Should().HaveCount(5);
    }

    [Fact]
    public async Task List_FavoritesOnly_ShouldReturnOnlyFavorites()
    {
        // FR-010 e cenário US4-6.
        var customer = await _fixture.CreateCustomerAsync(Token);

        var liked = await _fixture.AttachPhotoAsync(Token, customer.Id);
        await _fixture.AttachPhotoAsync(Token, customer.Id, "other.jpg");

        var favorite = await _fixture.PutAsync(
            $"{TestDataHelper.PhotoPath(customer.Id, liked.Photo.PhotoId)}/favorite", Token, new { });

        favorite.StatusCode.Should().Be(200, $"favoritar deveria funcionar: {favorite}");

        var page = (await _fixture.GetAsync(
            $"{TestDataHelper.PhotosPath(customer.Id)}?favorites=true", Token)).As<CustomerPhotoPage>();

        page.Items.Should().ContainSingle();
        page.Items[0].PhotoId.Should().Be(liked.Photo.PhotoId);
        page.Items[0].IsFavorite.Should().BeTrue();
    }

    [Fact]
    public async Task List_PhotoThatIsTheProfile_ShouldFlagIsProfile()
    {
        // A grade precisa saber qual foto é o perfil para o rótulo do editor (FR-011).
        var customer = await _fixture.CreateCustomerAsync(Token);
        var attached = await _fixture.AttachPhotoAsync(Token, customer.Id);
        var photoId = attached.Photo.PhotoId;

        var setProfile = await _fixture.PutAsync(
            TestDataHelper.ProfilePhotoPath(customer.Id), Token, new { photoId });

        setProfile.StatusCode.Should().Be(200, $"definir perfil deveria funcionar: {setProfile}");

        var page = (await _fixture.GetAsync(TestDataHelper.PhotosPath(customer.Id), Token))
            .As<CustomerPhotoPage>();

        page.Items.Single().IsProfile.Should().BeTrue();
    }

    [Fact]
    public async Task List_PageSizeAboveSixty_ShouldReturn400()
    {
        // Cada item da grade carrega o arquivo inteiro (sem miniatura), então a página tem teto.
        var customer = await _fixture.CreateCustomerAsync(Token);

        var response = await _fixture.GetAsync(
            $"{TestDataHelper.PhotosPath(customer.Id)}?pageSize=61", Token);

        response.StatusCode.Should().Be(400, $"página acima do limite deveria ser recusada: {response}");
        response.As<ErrorResult>().Erros.Should().ContainSingle()
            .Which.Campo.Should().Be("pageSize");
    }

    [Fact]
    public async Task List_OtherSellerCustomer_ShouldReturn404Indistinguishable()
    {
        var customer = await _fixture.CreateCustomerAsync(Token);
        var (tokenB, _) = await _fixture.NewSellerAsync("Vendedora B");

        var asOther = await _fixture.GetAsync(TestDataHelper.PhotosPath(customer.Id), tokenB);
        var asMissing = await _fixture.GetAsync(TestDataHelper.PhotosPath(999_999_999), tokenB);

        asOther.StatusCode.Should().Be(404);
        asMissing.StatusCode.Should().Be(404);
        asOther.Body.Should().Be(asMissing.Body,
            "a resposta não pode revelar que a cliente existe (SC-006)");
    }

    [Fact]
    public async Task List_PhotoUrl_ShouldBeRenewableThroughTheImageRoute()
    {
        // Research R10: a renovação de URL de uma foto é GET /images/{imageId}, que já existe na 004.
        // Este teste é o que garante que a rota de fotos não precisa de nada além disso.
        var customer = await _fixture.CreateCustomerAsync(Token);
        var image = await _fixture.UploadImageAsync(Token);

        await _fixture.AttachPhotoRawAsync(Token, customer.Id, image.ImageId);

        var page = (await _fixture.GetAsync(TestDataHelper.PhotosPath(customer.Id), Token))
            .As<CustomerPhotoPage>();

        var renewed = await _fixture.GetAsync($"/images/{page.Items[0].ImageId}", Token);

        renewed.StatusCode.Should().Be(200, $"renovar por imageId deveria funcionar: {renewed}");

        var asImage = renewed.As<ImageInfo>();

        asImage.ImageId.Should().Be(page.Items[0].ImageId);
        asImage.Url.Should().NotBeNullOrWhiteSpace();

        // As duas assinam o MESMO objeto. Não se pode afirmar que as strings diferem: a assinatura
        // SigV4 é determinística para (chave, data em segundos, expiração), então duas chamadas no
        // mesmo segundo produzem URLs idênticas. O que caracteriza "assinada a cada resposta, nunca
        // persistida" (FR-026) é a ausência de URL em qualquer coluna do banco — o objeto é sempre o
        // mesmo, o link é que nasce na resposta.
        asImage.Url.Should().Contain("X-Amz-Signature");
        page.Items[0].Url.Should().Contain("X-Amz-Signature");

        Func<string, string> keyOf = url => url.Split('?')[0];

        keyOf(asImage.Url!).Should().Be(keyOf(page.Items[0].Url!), "a renovação serve o mesmo arquivo");
    }

    // ---------------------------------------------------------------------
    // Foto de perfil (US3 — contracts/api.md §6 e §7)
    // ---------------------------------------------------------------------

    [Fact]
    public async Task SetProfile_WithoutCropImage_ShouldUseThePhotoOwnFile()
    {
        // FR-013: canal sem navegador (a conversa, e esta API chamada sem `imageId`) não tem recorte —
        // o círculo passa a usar o arquivo da própria foto.
        var customer = await _fixture.CreateCustomerAsync(Token);
        var attached = await _fixture.AttachPhotoAsync(Token, customer.Id);

        var response = await _fixture.PutAsync(
            TestDataHelper.ProfilePhotoPath(customer.Id), Token,
            new { photoId = attached.Photo.PhotoId });

        response.StatusCode.Should().Be(200, $"definir perfil deveria funcionar: {response}");

        var profile = response.As<CustomerProfilePhotoInfo>();

        profile.PhotoId.Should().Be(attached.Photo.PhotoId);
        profile.ImageId.Should().Be(attached.Photo.ImageId, "sem recorte, o perfil é o arquivo da foto");
        profile.OriginalImageId.Should().Be(attached.Photo.ImageId);
        profile.Url.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task SetProfile_WithBrowserCropImage_ShouldKeepBothLinks()
    {
        // FR-014: o perfil aponta para a foto do álbum E para o recorte, porque tocar no avatar abre a
        // original inteira (cenário US3-4). O recorte é um arquivo qualquer já gravado — a API não
        // sabe nem precisa saber que ele nasceu num canvas (research R4).
        var customer = await _fixture.CreateCustomerAsync(Token);
        var attached = await _fixture.AttachPhotoAsync(Token, customer.Id);
        var crop = await _fixture.UploadImageAsync(Token, "small.png", "image/png");

        var response = await _fixture.PutAsync(
            TestDataHelper.ProfilePhotoPath(customer.Id), Token,
            new { photoId = attached.Photo.PhotoId, imageId = crop.ImageId });

        response.StatusCode.Should().Be(200, $"definir perfil com recorte deveria funcionar: {response}");

        var profile = response.As<CustomerProfilePhotoInfo>();

        profile.ImageId.Should().Be(crop.ImageId);
        profile.OriginalImageId.Should().Be(attached.Photo.ImageId);
        profile.Url.Should().NotBeNullOrWhiteSpace();
        profile.OriginalUrl.Should().NotBeNullOrWhiteSpace("o detalhe precisa do link do original");
    }

    [Fact]
    public async Task SetProfile_PhotoOfAnotherCustomer_ShouldReturn404()
    {
        // `profile_photo_id` é FK e o banco não sabe que o álbum é de outra cliente: a regra é do
        // serviço (data-model §2), e responde com o 404 de sempre, sem distinguir.
        var ana = await _fixture.CreateCustomerAsync(Token);
        var bruna = await _fixture.CreateCustomerAsync(Token);
        var photoOfAna = await _fixture.AttachPhotoAsync(Token, ana.Id);

        var response = await _fixture.PutAsync(
            TestDataHelper.ProfilePhotoPath(bruna.Id), Token,
            new { photoId = photoOfAna.Photo.PhotoId });

        response.StatusCode.Should().Be(404, $"foto de outra cliente não deveria virar perfil: {response}");
        response.As<ErrorResult>().Mensagem.Should().Be("Foto não encontrada.");
    }

    [Fact]
    public async Task SetProfile_ShouldAppearInCustomerDetailListAndOrder()
    {
        // FR-016: onde havia iniciais passa a haver círculo — na lista, no perfil e no pedido.
        var customer = await _fixture.CreateCustomerAsync(Token);
        var attached = await _fixture.AttachPhotoAsync(Token, customer.Id);

        await _fixture.PutAsync(
            TestDataHelper.ProfilePhotoPath(customer.Id), Token,
            new { photoId = attached.Photo.PhotoId });

        var detail = (await _fixture.GetAsync($"/customers/{customer.Id}", Token)).As<CustomerDetailInfo>();

        detail.ProfilePhoto.Should().NotBeNull();
        detail.ProfilePhoto!.PhotoId.Should().Be(attached.Photo.PhotoId);

        // Sem recorte dedicado não há segunda versão do arquivo: `originalUrl` é opcional por contrato
        // (data-model §6) e a tela usa `originalUrl ?? url`. O caso com recorte e dois links diferentes
        // é SetProfile_WithBrowserCropImage_ShouldKeepBothLinks.
        detail.ProfilePhoto.OriginalImageId.Should().Be(attached.Photo.ImageId);
        detail.ProfilePhoto.Url.Should().NotBeNullOrWhiteSpace();
        detail.PhotoCount.Should().Be(1);

        var order = await _fixture.PostAsync("/orders", Token, TestDataHelper.NewSaleOrder(customer.Id));
        order.StatusCode.Should().Be(201, $"criar pedido de teste deveria funcionar: {order}");

        var orderDetail = (await _fixture.GetAsync($"/orders/{order.As<OrderInfo>().Id}", Token))
            .As<OrderDetailInfo>();

        orderDetail.Customer.ProfilePhoto.Should().NotBeNull(
            "a tela de pedido mostra a cliente com a foto dela");
        orderDetail.Customer.ProfilePhoto!.OriginalUrl.Should().BeNull(
            "fora do perfil não se assina o original: é uma URL a mais por linha sem uso (data-model §6)");
    }

    [Fact]
    public async Task SetProfile_ReplacingTheCrop_ShouldDropTheOrphanCropFile()
    {
        // Research R6: o recorte velho não está no álbum nem em pedido — trocar de perfil tem de tirá-lo
        // do storage, senão arquivo de cliente se acumula sem dono.
        var customer = await _fixture.CreateCustomerAsync(Token);
        var attached = await _fixture.AttachPhotoAsync(Token, customer.Id);
        var firstCrop = await _fixture.UploadImageAsync(Token, "small.png", "image/png");
        var secondCrop = await _fixture.UploadImageAsync(Token, "other.jpg", "image/jpeg");

        await _fixture.PutAsync(
            TestDataHelper.ProfilePhotoPath(customer.Id), Token,
            new { photoId = attached.Photo.PhotoId, imageId = firstCrop.ImageId });

        var second = await _fixture.PutAsync(
            TestDataHelper.ProfilePhotoPath(customer.Id), Token,
            new { photoId = attached.Photo.PhotoId, imageId = secondCrop.ImageId });

        second.StatusCode.Should().Be(200);

        // A prova é pelo metadado: sem linha em stored_images a rota de imagem responde 404, e a 004 é
        // quem garante que o objeto sai antes do metadado (FR-016 dela).
        var gone = await _fixture.GetAsync($"/images/{firstCrop.ImageId}", Token);

        gone.StatusCode.Should().Be(404, $"o recorte substituído e sem uso deveria sumir: {gone}");

        var kept = await _fixture.GetAsync($"/images/{secondCrop.ImageId}", Token);

        kept.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task SetProfile_KeepsTheAlbumPhotoAndRemoveProfileDoesNotDeleteIt()
    {
        // FR-015: remover o perfil não remove a foto do álbum.
        var customer = await _fixture.CreateCustomerAsync(Token);
        var attached = await _fixture.AttachPhotoAsync(Token, customer.Id);

        await _fixture.PutAsync(
            TestDataHelper.ProfilePhotoPath(customer.Id), Token,
            new { photoId = attached.Photo.PhotoId });

        var removed = await _fixture.DeleteAsync(TestDataHelper.ProfilePhotoPath(customer.Id), Token);

        removed.StatusCode.Should().Be(204, $"remover perfil deveria dar 204: {removed}");

        var page = (await _fixture.GetAsync(TestDataHelper.PhotosPath(customer.Id), Token))
            .As<CustomerPhotoPage>();

        page.Items.Should().ContainSingle();
        page.Items[0].IsProfile.Should().BeFalse();

        var imageStillThere = await _fixture.GetAsync($"/images/{attached.Photo.ImageId}", Token);

        imageStillThere.StatusCode.Should().Be(200, "a foto do álbum continua existindo");
    }

    [Fact]
    public async Task Delete_ProfileSourcePhoto_ShouldClearTheProfile()
    {
        // Edge case "foto de perfil excluída": a cliente volta às iniciais, sem cascata no banco.
        var customer = await _fixture.CreateCustomerAsync(Token);
        var attached = await _fixture.AttachPhotoAsync(Token, customer.Id);
        var crop = await _fixture.UploadImageAsync(Token, "small.png", "image/png");

        await _fixture.PutAsync(
            TestDataHelper.ProfilePhotoPath(customer.Id), Token,
            new { photoId = attached.Photo.PhotoId, imageId = crop.ImageId });

        var deleted = await _fixture.DeleteAsync(
            TestDataHelper.PhotoPath(customer.Id, attached.Photo.PhotoId), Token);

        deleted.StatusCode.Should().Be(204, $"excluir a foto deveria funcionar: {deleted}");

        var detail = (await _fixture.GetAsync($"/customers/{customer.Id}", Token)).As<CustomerDetailInfo>();

        detail.ProfilePhoto.Should().BeNull("o perfil seguiu a foto excluída (FR-015)");
        detail.PhotoCount.Should().Be(0);

        // Os dois arquivos do caso (foto e recorte) ficaram sem nenhum uso.
        (await _fixture.GetAsync($"/images/{attached.Photo.ImageId}", Token)).StatusCode.Should().Be(404);
        (await _fixture.GetAsync($"/images/{crop.ImageId}", Token)).StatusCode.Should().Be(404);
    }

    // ---------------------------------------------------------------------
    // Descrição, favorita e exclusão (US4 — contracts/api.md §3, §4, §5)
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData(500)]
    [InlineData(501)]
    public async Task UpdateDescription_LengthBoundary_ShouldAcceptOrFailWithField(int length)
    {
        // FR-007 e cenário US4-5: o limite é 500, e a recusa cita o campo.
        var customer = await _fixture.CreateCustomerAsync(Token);
        var attached = await _fixture.AttachPhotoAsync(Token, customer.Id);

        var response = await _fixture.PutAsync(
            $"{TestDataHelper.PhotoPath(customer.Id, attached.Photo.PhotoId)}/description", Token,
            new { description = new string('a', length) });

        if (length == 500)
        {
            response.StatusCode.Should().Be(200, $"500 caracteres é o limite aceito: {response}");
            response.As<CustomerPhotoInfo>().Description.Should().HaveLength(500);

            return;
        }

        response.StatusCode.Should().Be(400, $"501 caracteres deveria ser recusado: {response}");

        var error = response.As<ErrorResult>().Erros.Should().ContainSingle().Which;

        error.Campo.Should().Be("description");
        error.Mensagem.Should().Be("A descrição deve ter no máximo 500 caracteres.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task UpdateDescription_Blank_ShouldClearToNull(string? description)
    {
        // Cenário US4-4: "sem descrição" é ausência, não texto vazio.
        var customer = await _fixture.CreateCustomerAsync(Token);
        var attached = await _fixture.AttachPhotoAsync(Token, customer.Id, description: "saia verde");

        var response = await _fixture.PutAsync(
            $"{TestDataHelper.PhotoPath(customer.Id, attached.Photo.PhotoId)}/description", Token,
            new { description });

        response.StatusCode.Should().Be(200, $"apagar a descrição deveria funcionar: {response}");
        response.As<CustomerPhotoInfo>().Description.Should().BeNull();

        var page = (await _fixture.GetAsync(TestDataHelper.PhotosPath(customer.Id), Token))
            .As<CustomerPhotoPage>();

        page.Items[0].Description.Should().BeNull("a gravação é nula, não string vazia");
    }

    [Fact]
    public async Task Favorite_Twice_ShouldNotChangeUpdatedAtAgain()
    {
        // FR-008: idempotente. Refazer o pedido não pode embaralhar a ordem de "última alteração".
        var customer = await _fixture.CreateCustomerAsync(Token);
        var attached = await _fixture.AttachPhotoAsync(Token, customer.Id);
        var path = $"{TestDataHelper.PhotoPath(customer.Id, attached.Photo.PhotoId)}/favorite";

        var first = await _fixture.PutAsync(path, Token, new { });
        var second = await _fixture.PutAsync(path, Token, new { });

        first.StatusCode.Should().Be(200, $"favoritar deveria funcionar: {first}");
        second.StatusCode.Should().Be(200, $"favoritar de novo não deveria falhar: {second}");

        var once = first.As<CustomerPhotoInfo>();
        var twice = second.As<CustomerPhotoInfo>();

        once.IsFavorite.Should().BeTrue();
        twice.IsFavorite.Should().BeTrue();
        twice.UpdatedAt.Should().Be(once.UpdatedAt, "repetir o mesmo estado não é uma alteração");

        var unmarked = await _fixture.DeleteAsync(path, Token);

        unmarked.StatusCode.Should().Be(200, $"desfavoritar deveria funcionar: {unmarked}");
        unmarked.As<CustomerPhotoInfo>().IsFavorite.Should().BeFalse();

        var stillUnmarked = await _fixture.DeleteAsync(path, Token);

        stillUnmarked.As<CustomerPhotoInfo>().UpdatedAt.Should().Be(unmarked.As<CustomerPhotoInfo>().UpdatedAt);
    }

    [Fact]
    public async Task Favorite_PhotoOfAnotherSeller_ShouldReturn404AndLeaveThePhotoAlone()
    {
        var customer = await _fixture.CreateCustomerAsync(Token);
        var attached = await _fixture.AttachPhotoAsync(Token, customer.Id);
        var (tokenB, _) = await _fixture.NewSellerAsync("Vendedora B");

        var response = await _fixture.PutAsync(
            $"{TestDataHelper.PhotoPath(customer.Id, attached.Photo.PhotoId)}/favorite", tokenB, new { });

        response.StatusCode.Should().Be(404, $"foto alheia não deveria ser favoritada: {response}");

        var page = (await _fixture.GetAsync(TestDataHelper.PhotosPath(customer.Id), Token)).As<CustomerPhotoPage>();

        page.Items[0].IsFavorite.Should().BeFalse("nada mudou no álbum da outra vendedora");
    }

    [Fact]
    public async Task Delete_PhotoUsedByAnotherCustomer_ShouldKeepTheFile()
    {
        // FR-009 + edge case "mesma foto em duas clientes": apagar de um álbum não pode tirar a foto do
        // outro, porque as duas linhas apontam para o mesmo stored_image (research R6).
        var ana = await _fixture.CreateCustomerAsync(Token);
        var bruna = await _fixture.CreateCustomerAsync(Token);
        var image = await _fixture.UploadImageAsync(Token);

        await _fixture.AttachPhotoRawAsync(Token, ana.Id, image.ImageId);
        var forBruna = await _fixture.AttachPhotoRawAsync(Token, bruna.Id, image.ImageId);

        var deleted = await _fixture.DeleteAsync(
            TestDataHelper.PhotoPath(bruna.Id, forBruna.As<CustomerPhotoInsertResult>().Photo.PhotoId), Token);

        deleted.StatusCode.Should().Be(204, $"excluir a foto de Bruna deveria funcionar: {deleted}");

        var anaPage = (await _fixture.GetAsync(TestDataHelper.PhotosPath(ana.Id), Token)).As<CustomerPhotoPage>();

        anaPage.Items.Should().ContainSingle();
        anaPage.Items[0].Url.Should().NotBeNullOrWhiteSpace(
            "o arquivo continuou no storage porque ainda há uso (FR-009)");

        var imageStillThere = await _fixture.GetAsync($"/images/{image.ImageId}", Token);

        imageStillThere.StatusCode.Should().Be(200, "a imagem compartilhada não poderia ter sido apagada");
    }

    [Fact]
    public async Task Delete_UnknownPhoto_ShouldReturn404WithNotFoundMessage()
    {
        var customer = await _fixture.CreateCustomerAsync(Token);

        var response = await _fixture.DeleteAsync(TestDataHelper.PhotoPath(customer.Id, 999_999_999), Token);

        response.StatusCode.Should().Be(404);
        response.As<ErrorResult>().Mensagem.Should().Be("Foto não encontrada.");
    }
}
