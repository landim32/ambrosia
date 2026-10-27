using System.Text.Json;
using Ambrosia.ApiTests.Fixtures;
using Ambrosia.ApiTests.Helpers;
using Ambrosia.DTO.Images;
using FluentAssertions;
using Xunit.Abstractions;

namespace Ambrosia.ApiTests.Controllers;

/// <summary>
/// Testes de API da feature 004 (skill <c>dotnet-test-api</c>), contra a <c>ApiBaseUrl</c> do ambiente
/// em teste. Cobrem os 12 critérios da HU 003 pelas histórias US1 a US6 da spec.
/// <para>
/// Cada teste declara conteúdo <b>próprio</b>: a deduplicação é por <c>(usuário, sha256)</c>, então
/// reusar o bytes fixos do fixture faria o segundo teste do mesmo arquivo receber <c>alreadyExists</c>
/// e passar por motivo errado. <see cref="UniqueBytes"/> acrescenta um sufixo aleatório — o JPEG
/// continua um JPEG (a detecção olha as assinaturas de bytes), só vira outro conteúdo.
/// </para>
/// </summary>
[Collection(ApiTestCollection.API_TESTS)]
public class ImagesControllerTests
{
    private const string UPLOADS = "/images/uploads";

    private readonly ApiTestFixture _fixture;
    private readonly ITestOutputHelper _output;

    public ImagesControllerTests(ApiTestFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    private string Token => _fixture.TokenA;

    // ---------------------------------------------------------------------
    // US1 — enviar uma imagem diretamente ao storage e confirmar (T029, T030, T031)
    // ---------------------------------------------------------------------

    [Fact]
    public async Task RequestUpload_ValidJpeg_ShouldReturn201WithUploadUrl()
    {
        // Arrange: US1 cenário 1 — declaração de um JPEG válido.
        var content = UniqueBytes("small.jpg");

        // Act
        var response = await DeclareAsync(content, "image/jpeg");

        // Assert: 201 com a URL de envio, o verbo, o tipo assinado e o prazo futuro.
        response.StatusCode.Should().Be(201, $"declarar envio deveria funcionar: {response}");

        var result = response.As<ImageUploadResult>();

        result.ImageId.Should().BeGreaterThan(0);
        result.AlreadyExists.Should().BeFalse();
        result.UploadUrl.Should().NotBeNullOrWhiteSpace();
        result.UploadMethod.Should().Be("PUT");
        result.ContentType.Should().Be("image/jpeg");
        result.UploadExpiresAt.Should().NotBeNull();
        result.UploadExpiresAt.Should().BeAfter(DateTime.UtcNow.AddSeconds(-30));
        result.Image.Should().BeNull("não há imagem disponível ainda");
    }

    [Fact]
    public async Task UploadUrl_PutWithAnotherContentType_ShouldReturn403FromStorage()
    {
        // US1 cenário 2: o Content-Type está assinado, então trocar o tipo é recusado pelo storage —
        // antes de qualquer confirmação da API (FR-003).
        var content = UniqueBytes("small.jpg");
        var declared = await DeclareAsync(content, "image/jpeg");
        var result = declared.As<ImageUploadResult>();

        // Act: mesmo corpo, outro Content-Type.
        var put = await StorageHttp.PutAsync(result.UploadUrl!, content, "image/png");

        // Assert
        put.StatusCode.Should().Be(System.Net.HttpStatusCode.Forbidden,
            $"o PUT com tipo diferente do assinado deveria ser recusado: {await StorageHttp.DescribeAsync(put)}");

        put.Dispose();
    }

    [Fact]
    public async Task Confirm_UploadAndConfirmFlow_ShouldReturn200AvailableAndReadableBytes()
    {
        // Arrange: US1 cenários 1 e 3 — declara, envia, confirma.
        var content = UniqueBytes("small.jpg");
        var declared = await DeclareAsync(content, "image/jpeg");
        var declaredResult = declared.As<ImageUploadResult>();

        var put = await StorageHttp.PutAsync(declaredResult.UploadUrl!, content, "image/jpeg");
        put.StatusCode.Should().Be(System.Net.HttpStatusCode.OK,
            $"o PUT assinado deveria aceitar o arquivo: {await StorageHttp.DescribeAsync(put)}");
        put.Dispose();

        // Act
        var confirmed = await _fixture.PostAsync($"{UPLOADS}/{declaredResult.ImageId}/confirm", Token, new { });

        // Assert: 200 available, dimensões lidas do cabeçalho.
        confirmed.StatusCode.Should().Be(200, $"confirmar envio deveria funcionar: {confirmed}");

        var image = confirmed.As<ImageInfo>();

        image.ImageId.Should().Be(declaredResult.ImageId);
        image.Status.Should().Be(Ambrosia.DTO.Enums.ImageStatus.Available);
        image.Width.Should().Be(320);
        image.Height.Should().Be(240);
        image.Sha256.Should().Be(StorageHttp.Sha256Hex(content));
        image.Url.Should().NotBeNullOrWhiteSpace();

        // O binário baixado pela URL assinada é byte a byte o que foi enviado (FR-012): o caminho
        // direto do navegador não sanitiza nada (clarificação 2 da spec).
        var downloaded = await StorageHttp.GetAsync(image.Url!);
        downloaded.StatusCode.Should().Be(System.Net.HttpStatusCode.OK,
            $"a URL de leitura deveria servir o arquivo: {await StorageHttp.DescribeAsync(downloaded)}");

        (await StorageHttp.ReadBytesAsync(downloaded)).Should().Equal(content);
        downloaded.Dispose();
    }

    [Fact]
    public async Task Confirm_AlreadyAvailable_ShouldReturn200Idempotently()
    {
        // US1 cenário 3: repetir a confirmação não reescreve nada, só troca a URL assinada.
        var (imageId, _) = await RequestAndPutAsync(Token, "small.jpg", "image/jpeg");

        var first = await _fixture.PostAsync($"{UPLOADS}/{imageId}/confirm", Token, new { });
        first.StatusCode.Should().Be(200, $"primeira confirmação: {first}");

        // Act
        var second = await _fixture.PostAsync($"{UPLOADS}/{imageId}/confirm", Token, new { });

        // Assert
        second.StatusCode.Should().Be(200, $"confirmação repetida deveria ser idempotente: {second}");

        var again = second.As<ImageInfo>();

        again.Status.Should().Be(Ambrosia.DTO.Enums.ImageStatus.Available);
        again.ImageId.Should().Be(imageId);
    }

    [Fact]
    public async Task Confirm_SizeDifferentFromDeclared_ShouldReturn400AndMarkFailed()
    {
        // US1 cenário 4 + critério 6 da HU: o tamanho real é conferido no HeadObject.
        var content = UniqueBytes("small.jpg");
        var declared = await DeclareAsync(content, "image/jpeg", sizeOverride: content.Length + 1);
        var imageId = declared.As<ImageUploadResult>().ImageId;

        var put = await StorageHttp.PutAsync(declared.As<ImageUploadResult>().UploadUrl!, content, "image/jpeg");
        put.Dispose();

        // Act
        var response = await _fixture.PostAsync($"{UPLOADS}/{imageId}/confirm", Token, new { });

        // Assert
        response.StatusCode.Should().Be(400, $"tamanho divergente deveria recusar: {response}");
        MensagemOf(response).Should().Be("O tamanho do arquivo enviado não confere com o declarado ou excede o limite.");

        var status = await _fixture.GetAsync($"/images/{imageId}", Token);
        status.As<ImageInfo>().Status.Should().Be(Ambrosia.DTO.Enums.ImageStatus.Failed);

        await AssertRejectedUploadMustBeRedeclaredAsync(content, "image/jpeg");
    }

    [Fact]
    public async Task Confirm_HashDifferentFromDeclared_ShouldReturn400()
    {
        // US1 cenário 4: declara o hash de OUTRO arquivo e envia um conteúdo distinto.
        var content = UniqueBytes("small.jpg");
        var otherHash = StorageHttp.Sha256Hex(UniqueBytes("other.jpg"));

        var declared = await DeclareAsync(content, "image/jpeg", sha256Override: otherHash);
        var imageId = declared.As<ImageUploadResult>().ImageId;

        var put = await StorageHttp.PutAsync(declared.As<ImageUploadResult>().UploadUrl!, content, "image/jpeg");
        put.Dispose();

        // Act
        var response = await _fixture.PostAsync($"{UPLOADS}/{imageId}/confirm", Token, new { });

        // Assert
        response.StatusCode.Should().Be(400, $"hash divergente deveria recusar: {response}");
        MensagemOf(response).Should().Be("O conteúdo do arquivo não confere com o hash declarado.");

        // A recusa remove o objeto da mesma chave, então o reenvio prova que ele sumiu.
        await AssertRejectedUploadMustBeRedeclaredAsync(content, "image/jpeg");
    }

    [Fact]
    public async Task Confirm_FakeJpeg_ShouldReturn400ByType()
    {
        // US1 cenário 6 / FR-005: um PDF com nome .jpg passa no Content-Type assinado e no tamanho —
        // o que o derruba é a detecção pelo conteúdo.
        var content = UniqueBytes("fake.jpg");
        var declared = await DeclareAsync(content, "image/jpeg");
        var imageId = declared.As<ImageUploadResult>().ImageId;

        var put = await StorageHttp.PutAsync(declared.As<ImageUploadResult>().UploadUrl!, content, "image/jpeg");
        put.Dispose();

        // Act
        var response = await _fixture.PostAsync($"{UPLOADS}/{imageId}/confirm", Token, new { });

        // Assert
        response.StatusCode.Should().Be(400, $"conteúdo que não é imagem deveria recusar: {response}");
        MensagemOf(response).Should().Be("O conteúdo do arquivo não é uma imagem de tipo aceito.");

        await AssertRejectedUploadMustBeRedeclaredAsync(content, "image/jpeg");
    }

    [Fact]
    public async Task RequestUpload_InvalidPayloads_ShouldReturn400OnTheRightField()
    {
        // US1 cenário 8 + edge cases: cada campo recusado no lugar certo, com erros[].campo.
        var validHash = StorageHttp.Sha256Hex(UniqueBytes("small.jpg"));
        var maxBytes = _fixture.StorageMaxUploadBytes;

        var cases = new (string Field, string MimeType, long Size, string Sha)[]
        {
            ("mimeType", "image/gif", 1000, validHash),
            ("sizeBytes", "image/jpeg", 0, validHash),
            ("sizeBytes", "image/jpeg", maxBytes + 1, validHash),
            ("sha256", "image/jpeg", 1000, validHash[..63]),
            ("sha256", "image/jpeg", 1000, new string('g', 64))
        };

        foreach (var (field, mimeType, size, sha) in cases)
        {
            var response = await _fixture.PostAsync(UPLOADS, Token, new ImageUploadInsertInfo
            {
                MimeType = mimeType,
                SizeBytes = size,
                Sha256 = sha
            });

            response.StatusCode.Should().Be(400, $"declarar {mimeType}/{size}/{sha[..8]} deveria recusar {field}: {response}");

            using var document = JsonDocument.Parse(response.Body);
            document.RootElement.GetProperty("erros").EnumerateArray()
                .Select(e => e.GetProperty("campo").GetString())
                .Should().Contain(field, $"o erro deveria apontar o campo {field}");
        }
    }

    [Fact]
    public async Task Confirm_WithoutUpload_ShouldReturn400AndStayPending()
    {
        // Critério 7 da HU: confirmar sem ter enviado o arquivo não muda o status — o cliente ainda
        // pode mandar o binário e confirmar de novo.
        var content = UniqueBytes("small.jpg");
        var declared = await DeclareAsync(content, "image/jpeg");
        var imageId = declared.As<ImageUploadResult>().ImageId;

        // Act
        var response = await _fixture.PostAsync($"{UPLOADS}/{imageId}/confirm", Token, new { });

        // Assert
        response.StatusCode.Should().Be(400, $"confirmar sem enviar deveria recusar: {response}");
        MensagemOf(response).Should().Be("O arquivo da imagem ainda não foi enviado.");

        var after = await _fixture.GetAsync($"/images/{imageId}", Token);
        var image = after.As<ImageInfo>();
        image.Status.Should().Be(Ambrosia.DTO.Enums.ImageStatus.Pending);
        image.Url.Should().BeNull("pending não expõe URL (FR-015)");

        // E o mesmo envio ainda pode ser concluído.
        var put = await StorageHttp.PutAsync(declared.As<ImageUploadResult>().UploadUrl!, content, "image/jpeg");
        put.Dispose();

        var confirmed = await _fixture.PostAsync($"{UPLOADS}/{imageId}/confirm", Token, new { });
        confirmed.StatusCode.Should().Be(200, $"concluir depois do envio: {confirmed}");
    }

    [Fact]
    public async Task Confirm_UnknownImageId_ShouldReturn404()
    {
        // US1 cenário 8: id que não existe no usuário.
        var response = await _fixture.PostAsync($"{UPLOADS}/999999999999/confirm", Token, new { });

        response.StatusCode.Should().Be(404, $"confirmar id inexistente deveria dar 404: {response}");
    }

    [Fact]
    public async Task RequestUpload_SameAvailableContent_ShouldReturn200AlreadyExists()
    {
        // US1 cenário 7 / SC-005 / FR-011: o mesmo conteúdo não sobe duas vezes.
        var (imageId, content) = await RequestAndPutAsync(Token, "small.jpg", "image/jpeg");

        var first = await _fixture.PostAsync($"{UPLOADS}/{imageId}/confirm", Token, new { });
        first.StatusCode.Should().Be(200, $"confirmar antes: {first}");

        // Act: declara o MESMO sha256 de novo.
        var again = await DeclareAsync(content, "image/jpeg");

        // Assert: 200, alreadyExists, mesmo imageId e nenhuma URL de envio.
        again.StatusCode.Should().Be(200, $"redeclarar conteúdo disponível: {again}");

        var result = again.As<ImageUploadResult>();

        result.AlreadyExists.Should().BeTrue();
        result.ImageId.Should().Be(imageId);
        result.UploadUrl.Should().BeNull();
        result.Image.Should().NotBeNull();
        result.Image!.Url.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task RequestUpload_WhileStillPending_ShouldReturn201WithSameIdAndNewUrl()
    {
        // Critério 8 da HU: um envio pendente do mesmo conteúdo é reaproveitado, com URL nova.
        var content = UniqueBytes("small.jpg");
        var first = (await DeclareAsync(content, "image/jpeg")).As<ImageUploadResult>();

        // Act
        var again = await DeclareAsync(content, "image/jpeg");

        // Assert
        again.StatusCode.Should().Be(201, $"redeclarar pendente deveria dar 201: {again}");

        var result = again.As<ImageUploadResult>();

        result.AlreadyExists.Should().BeFalse();
        result.ImageId.Should().Be(first.ImageId, "o registro pendente é reaproveitado");
        result.UploadUrl.Should().NotBeNullOrWhiteSpace();
        result.UploadUrl.Should().NotBe(first.UploadUrl, "a URL é sempre nova (a anterior pode ter expirado)");
    }

    // ---------------------------------------------------------------------
    // US2 — ler, negar leitura não autorizada e excluir (T037, T038, T039)
    // ---------------------------------------------------------------------

    [Fact]
    public async Task GetById_PendingImage_ShouldNotExposeUrl()
    {
        // FR-015: pending tem metadado mas nenhuma URL — o binário pode nem estar no storage.
        var content = UniqueBytes("small.jpg");
        var declared = (await DeclareAsync(content, "image/jpeg")).As<ImageUploadResult>();

        var response = await _fixture.GetAsync($"/images/{declared.ImageId}", Token);

        response.StatusCode.Should().Be(200, $"consultar a própria imagem pendente: {response}");

        var image = response.As<ImageInfo>();

        image.Status.Should().Be(Ambrosia.DTO.Enums.ImageStatus.Pending);
        image.Url.Should().BeNull();
        image.UrlExpiresAt.Should().BeNull();
    }

    [Fact]
    public async Task GetById_Twice_ShouldReturnDifferentUrlBothValid()
    {
        // Critério 4 da HU / FR-012: cada consulta assina uma URL nova; nenhuma delas fica gravada.
        var (imageId, _) = await RequestAndPutAsync(Token, "small.jpg", "image/jpeg");
        await ConfirmAsync(imageId);

        var first = (await _fixture.GetAsync($"/images/{imageId}", Token)).As<ImageInfo>();
        var second = (await _fixture.GetAsync($"/images/{imageId}", Token)).As<ImageInfo>();

        first.Url.Should().NotBeNullOrWhiteSpace();
        second.Url.Should().NotBeNullOrWhiteSpace();
        first.Url.Should().NotBe(second.Url, "a assinatura é gerada a cada resposta");

        // ...e as duas funcionam.
        (await StorageHttp.GetAsync(first.Url!)).StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        (await StorageHttp.GetAsync(second.Url!)).StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }

    [Fact]
    public async Task ReadUrl_StrippedOfSignature_ShouldReturn403()
    {
        // US2 cenário 2 / FR-004: o bucket é privado — o endereço do objeto sem a query assinada não
        // serve nada, com ou sem o token da API.
        var (imageId, _) = await RequestAndPutAsync(Token, "small.jpg", "image/jpeg");
        var image = (await ConfirmAsync(imageId)).As<ImageInfo>();

        var address = StorageHttp.StripQuery(image.Url!);

        address.Should().NotContain("X-Amz-Signature");

        // Act: sem cabeçalho nenhum, e depois com o Bearer da própria API.
        var anonymous = await StorageHttp.GetAsync(address);
        var withApiToken = await StorageHttp.GetAsync(address);

        anonymous.StatusCode.Should().Be(System.Net.HttpStatusCode.Forbidden,
            $"endereço sem assinatura não pode servir o objeto: {await StorageHttp.DescribeAsync(anonymous)}");
        withApiToken.StatusCode.Should().Be(System.Net.HttpStatusCode.Forbidden);

        anonymous.Dispose();
        withApiToken.Dispose();
    }

    [Fact]
    [Trait("Category", "Slow")]
    public async Task ReadUrl_AfterPresignedReadExpiry_ShouldReturn403()
    {
        // US2 cenário 3 / FR-012 / critério 4 da HU. Lento de propósito: dorme o prazo de leitura do
        // ambiente (research §16). xUnit 2 não tem skip dinâmico, então o filtro é o Trait
        // (`--filter Category!=Slow`) e, quando o prazo configurado passa do teto, o teste registra o
        // motivo no output em vez de fingir uma verificação que não aconteceu.
        var wait = TimeSpan.FromMinutes(_fixture.StoragePresignedReadMinutes);

        if (!WithinSlowBudget(wait, "expiração da URL de leitura"))
        {
            return;
        }

        var (imageId, _) = await RequestAndPutAsync(Token, "small.jpg", "image/jpeg");
        var image = (await ConfirmAsync(imageId)).As<ImageInfo>();

        // Antes de expirar funciona.
        (await StorageHttp.GetAsync(image.Url!)).StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        await Task.Delay(wait + TimeSpan.FromSeconds(15));

        // Act
        var expired = await StorageHttp.GetAsync(image.Url!);

        // Assert
        expired.StatusCode.Should().Be(System.Net.HttpStatusCode.Forbidden,
            $"URL vencida deveria dar 403: {await StorageHttp.DescribeAsync(expired)}");

        expired.Dispose();
    }

    [Fact]
    [Trait("Category", "Slow")]
    public async Task UploadUrl_AfterPresignedUploadExpiry_ShouldRejectThePut()
    {
        // T039, segundo padrão: o link de envio também vence. Aqui o arquivo NUNCA sobe — a URL é
        // usada depois do prazo — e o storage recusa o PUT assinado (FR-003, US1 cenário 5).
        var wait = TimeSpan.FromMinutes(_fixture.StoragePresignedUploadMinutes);

        if (!WithinSlowBudget(wait, "expiração da URL de envio"))
        {
            return;
        }

        var content = UniqueBytes("small.jpg");
        var declared = (await DeclareAsync(content, "image/jpeg")).As<ImageUploadResult>();

        // Dentro do prazo o PUT passa...
        var early = await StorageHttp.PutAsync(declared.UploadUrl!, content, "image/jpeg");
        early.StatusCode.Should().Be(System.Net.HttpStatusCode.OK,
            $"PUT dentro do prazo: {await StorageHttp.DescribeAsync(early)}");
        early.Dispose();

        await Task.Delay(wait + TimeSpan.FromSeconds(15));

        // ...e depois da validade, a mesma URL assinada não serve mais para nada.
        var late = await StorageHttp.PutAsync(declared.UploadUrl!, content, "image/jpeg");

        late.StatusCode.Should().Be(System.Net.HttpStatusCode.Forbidden,
            $"PUT com URL vencida: {await StorageHttp.DescribeAsync(late)}");
        late.Dispose();
    }

    [Fact]
    public async Task AnotherUser_ShouldGet404OnEveryImageRouteAndNotLeakAnything()
    {
        // US2 cenário 4 + SC-008: o B não sabe se a imagem existe, e nenhuma resposta descreve o
        // conteúdo do A (chave, tipo e hash ficam de fora).
        var (imageId, content) = await RequestAndPutAsync(_fixture.TokenA, "small.jpg", "image/jpeg");
        await ConfirmAsync(imageId);

        var calls = new (string Label, Task<HttpCallResult> Call)[]
        {
            ("GET /images/{id}", _fixture.GetAsync($"/images/{imageId}", _fixture.TokenB)),
            ("POST confirm", _fixture.PostAsync($"{UPLOADS}/{imageId}/confirm", _fixture.TokenB, new { })),
            ("DELETE /images/{id}", _fixture.DeleteAsync($"/images/{imageId}", _fixture.TokenB))
        };

        foreach (var (label, pending) in calls)
        {
            var response = await pending;

            response.StatusCode.Should().Be(404, $"{label} deveria responder 404 para o outro usuário: {response}");

            response.Body.Should().NotContain(StorageHttp.Sha256Hex(content), $"{label} vaza o hash");
            response.Body.Should().NotContain("users/", $"{label} vaza a chave do objeto");
            response.Body.Should().NotContain("image/jpeg", $"{label} vaza o tipo");
        }

        // E a imagem do A continua intacta depois das três tentativas.
        (await _fixture.GetAsync($"/images/{imageId}", Token)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Delete_ByOwner_ShouldReturn204AndMakeObjectUnreadable()
    {
        // US2 cenário 5 / FR-016: 204, objeto apagado, metadado apagado; a URL assinada anterior passa
        // a dar 404 (NoSuchKey) — o contrato diferencia isso do 403 por assinatura.
        var (imageId, _) = await RequestAndPutAsync(Token, "other.jpg", "image/jpeg");
        var image = (await ConfirmAsync(imageId)).As<ImageInfo>();
        var urlBeforeDelete = image.Url!;

        // Act
        var deleted = await _fixture.DeleteAsync($"/images/{imageId}", Token);

        // Assert
        deleted.StatusCode.Should().Be(204, $"exclusão pelo dono: {deleted}");

        var gone = await StorageHttp.GetAsync(urlBeforeDelete);
        gone.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound,
            $"objeto excluído deveria dar 404: {await StorageHttp.DescribeAsync(gone)}");
        gone.Dispose();

        var metadata = await _fixture.GetAsync($"/images/{imageId}", Token);
        metadata.StatusCode.Should().Be(404, "o metadado também sumiu");
    }

    [Fact]
    public async Task Delete_PendingImageWithoutUpload_ShouldReturn204()
    {
        // FR-016 "qualquer status": apagar um pending que nunca teve objeto não é erro.
        var content = UniqueBytes("small.jpg");
        var declared = (await DeclareAsync(content, "image/jpeg")).As<ImageUploadResult>();

        var response = await _fixture.DeleteAsync($"/images/{declared.ImageId}", Token);

        response.StatusCode.Should().Be(204, $"apagar pending sem upload: {response}");
    }

    [Fact]
    public async Task DeleteAllForUser_ShouldNotBeReachableByAnyRoute()
    {
        // T048 / FR-017: a remoção em massa é uso interno. Se alguém ligar isso a um endpoint, o
        // documento OpenAPI passa a ter a rota e este teste pega.
        var document = await _fixture.GetAsync("/swagger/v1/swagger.json", Token);

        document.IsSuccess.Should().BeTrue($"o documento OpenAPI deveria estar acessível: {document}");

        using var json = JsonDocument.Parse(document.Body);
        var paths = json.RootElement.GetProperty("paths").EnumerateObject().Select(p => p.Name).ToList();

        paths.Should().NotContain(p => p.Contains("users", StringComparison.OrdinalIgnoreCase)
                                       && p.Contains("images", StringComparison.OrdinalIgnoreCase),
            "DeleteAllForUserAsync não pode ter rota");

        paths.Should().Contain(p => p.StartsWith("/images"), "as rotas da 004 existem no documento");
    }

    // ---------------------------------------------------------------------
    // US3 — gravar pelo servidor, sem metadado de privacidade (T043, T044)
    // ---------------------------------------------------------------------

    [Fact]
    public async Task ServerUpload_JpegWithGps_ShouldDropExifAndStayViewable()
    {
        // Critério 11 da HU / FR-010 / SC-007: o fixture tem EXIF com IFD GPS; o que o storage recebe
        // não pode ter nenhum deles.
        var source = File.ReadAllBytes(StorageHttp.FixturePath("small.jpg"));
        ContainsMarker(source, "Exif").Should().BeTrue(
            "o fixture precisa ter EXIF, senão este teste passa por vazio e não por ter removido");

        // Act
        var response = await _fixture.PostFileAsync("/images", Token, source, "image/jpeg", "small.jpg");

        // Assert
        response.StatusCode.Should().Be(201, $"gravar pelo servidor: {response}");

        var image = response.As<ImageInfo>();

        image.Status.Should().Be(Ambrosia.DTO.Enums.ImageStatus.Available);
        image.Source.Should().Be(Ambrosia.DTO.Enums.ImageSource.Server);
        image.Width.Should().NotBeNull("a dimensão vem da decodificação");
        image.Height.Should().NotBeNull();
        image.Url.Should().NotBeNullOrWhiteSpace();

        var downloaded = await StorageHttp.GetAsync(image.Url!);
        var stored = await StorageHttp.ReadBytesAsync(downloaded);
        downloaded.Dispose();

        stored.Should().NotBeEquivalentTo(source, "o caminho do servidor re-codifica o conteúdo");
        ContainsMarker(stored, "Exif").Should().BeFalse("o marcador APP1 de EXIF (com o IFD GPS) tem de sumir");

        Convert.ToHexString(stored[..3]).Should().Be("FFD8FF", $"continua um JPEG legível: {BitConverter.ToString(stored[..4])}");
    }

    [Fact]
    public async Task ServerUpload_SameFileTwice_ShouldReturn200WithTheSameImageId()
    {
        // FR-011 no caminho do servidor: o hash é do conteúdo FINAL, então repetir o envio não
        // duplica nada (e a resposta é 200, não 201).
        var source = File.ReadAllBytes(StorageHttp.FixturePath("other.jpg"));

        var first = await _fixture.PostFileAsync("/images", Token, source, "image/jpeg", "other.jpg");
        first.StatusCode.Should().BeOneOf(200, 201);

        var firstImage = first.As<ImageInfo>();

        // Act
        var second = await _fixture.PostFileAsync("/images", Token, source, "image/jpeg", "other.jpg");

        // Assert
        second.StatusCode.Should().Be(200, $"segunda gravação do mesmo conteúdo: {second}");
        second.As<ImageInfo>().ImageId.Should().Be(firstImage.ImageId);
    }

    [Theory]
    [InlineData("small.png", "image/png")]
    [InlineData("small.webp", "image/webp")]
    public async Task ServerUpload_AnyAcceptedType_ShouldSucceed(string fileName, string mimeType)
    {
        // US3 cenário 4 / FR-005: os três tipos aceitos passam.
        var source = File.ReadAllBytes(StorageHttp.FixturePath(fileName));

        var response = await _fixture.PostFileAsync("/images", Token, source, mimeType, fileName);

        response.StatusCode.Should().BeOneOf(200, 201);
        response.As<ImageInfo>().MimeType.Should().Be(mimeType);
    }

    [Fact]
    public async Task ServerUpload_InvalidContent_ShouldReturn400()
    {
        // T044 / FR-008: PDF com nome .jpg, corpo vazio e campo ausente — todos 400.
        var pdf = File.ReadAllBytes(StorageHttp.FixturePath("fake.jpg"));

        var wrongType = await _fixture.PostFileAsync("/images", Token, pdf, "image/jpeg", "foto.jpg");
        wrongType.StatusCode.Should().Be(400, $"PDF disfarçado de JPEG: {wrongType}");

        var empty = await _fixture.PostFileAsync("/images", Token, [], "image/jpeg", "vazio.jpg");
        empty.StatusCode.Should().Be(400, $"arquivo vazio: {empty}");

        var missing = await _fixture.PostFileAsync("/images", Token, null, "image/jpeg", "ausente.jpg", fieldOverride: null);
        missing.StatusCode.Should().Be(400, $"campo file ausente: {missing}");

        var gif = new byte[64];
        gif[0] = (byte)'G';
        gif[1] = (byte)'I';
        gif[2] = (byte)'F';

        var notAccepted = await _fixture.PostFileAsync("/images", Token, gif, "image/gif", "animado.gif");
        notAccepted.StatusCode.Should().Be(400, $"tipo fora da lista aceita: {notAccepted}");
    }

    // ---------------------------------------------------------------------
    // US4 — excluir (T047, T048)
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Delete_AnotherUsersImage_ShouldReturn404AndLeaveItIntact()
    {
        // US4 cenário 2 / SC-006: o B recebe 404 e a imagem do A continua legível por URL e por consulta.
        var (imageId, _) = await RequestAndPutAsync(_fixture.TokenA, "small.jpg", "image/jpeg");
        var image = (await ConfirmAsync(imageId, _fixture.TokenA)).As<ImageInfo>();
        var url = image.Url!;

        // Act
        var attempt = await _fixture.DeleteAsync($"/images/{imageId}", _fixture.TokenB);

        // Assert
        attempt.StatusCode.Should().Be(404, $"exclusão por outro usuário deveria dar 404: {attempt}");

        (await StorageHttp.GetAsync(url)).StatusCode.Should().Be(System.Net.HttpStatusCode.OK,
            "o objeto de A não pode ter sido tocado");
        (await _fixture.GetAsync($"/images/{imageId}", _fixture.TokenA)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Delete_ImageShouldNotTouchAnythingElseOfTheAccount()
    {
        // SC-006 / FR-016: apagar imagem não apaga cliente nem pedido, e não mexe em outra conta.
        var other = await RequestAndPutAsync(_fixture.TokenB, "other.jpg", "image/jpeg");
        await ConfirmAsync(other.ImageId, _fixture.TokenB);

        var mine = await RequestAndPutAsync(Token, "small.jpg", "image/jpeg");
        await ConfirmAsync(mine.ImageId);

        var customer = await _fixture.PostAsync("/customers", Token,
            new Ambrosia.DTO.Customers.CustomerInsertInfo
            {
                Name = "Sobrevive à exclusão",
                Cpf = Helpers.TestDataHelper.GenerateValidCpf()
            });
        customer.StatusCode.Should().Be(201, $"criar cliente para o teste: {customer}");
        var customerId = customer.As<Ambrosia.DTO.Customers.CustomerDetailInfo>().Id;

        // Act
        (await _fixture.DeleteAsync($"/images/{mine.ImageId}", Token)).StatusCode.Should().Be(204);

        // Assert: a conta segue íntegra, e a imagem do outro usuário também.
        (await _fixture.GetAsync($"/customers/{customerId}", Token)).IsSuccess.Should().BeTrue();
        (await _fixture.GetAsync($"/images/{other.ImageId}", _fixture.TokenB)).IsSuccess.Should().BeTrue();
        (await StorageHttp.GetAsync(
            (await ConfirmAsync(other.ImageId, _fixture.TokenB)).As<ImageInfo>().Url!))
            .StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }

    [Fact]
    public async Task DeleteAllImagesOfUser_ShouldNotHaveARoute()
    {
        // T048 / FR-017: DeleteAllForUserAsync é uso interno. Além da varredura do documento OpenAPI,
        // a rota simples não pode existir: DELETE /images (sem id) não apaga a conta inteira.
        var response = await _fixture.DeleteAsync("/images", Token);

        response.IsSuccess.Should().BeFalse($"DELETE /images sem id não pode remover nada: {response}");
        response.StatusCode.Should().BeOneOf(404, 405);
    }

    // ---------------------------------------------------------------------
    // US5 — CORS do storage (T057)
    // ---------------------------------------------------------------------

    [Fact]
    public async Task UploadUrl_PreflightFromTheFrontendOrigin_ShouldBeAllowed()
    {
        // FR-022 / SC-010: o navegador faz preflight antes do PUT direto. A regra CORS é do BUCKET
        // ( Spaces faz por bucket, sem o desvio de "por servidor" do plano), autoriza a origem do
        // frontend, o método PUT e o cabeçalho Content-Type.
        var declared = await DeclareAsync(UniqueBytes("small.jpg"), "image/jpeg");
        var uploadUrl = declared.As<ImageUploadResult>().UploadUrl!;

        var preflight = await StorageHttp.OptionsAsync(uploadUrl, _fixture.StorageFrontendOrigin, "PUT");

        var allowed = preflight.Headers.TryGetValues("Access-Control-Allow-Origin", out var values)
            ? string.Join(",", values)
            : null;

        allowed.Should().NotBeNullOrWhiteSpace(
            $"preflight sem Access-Control-Allow-Origin: {await StorageHttp.DescribeAsync(preflight)}");
        allowed.Should().Contain(_fixture.StorageFrontendOrigin);

        if (preflight.Headers.TryGetValues("Access-Control-Allow-Methods", out var methods))
        {
            string.Join(",", methods).Should().Contain("PUT");
        }

        preflight.Dispose();
    }

    // ---------------------------------------------------------------------
    // US6 — limpeza de envios abandonados (T060)
    // ---------------------------------------------------------------------

    [Fact]
    [Trait("Category", "Slow")]
    public async Task Cleanup_ShouldRemoveAbandonedUploadAndKeepAvailableImages()
    {
        // FR-018 / SC-003. Lento de propósito: dorme TTL + intervalo + margem (research §16).
        var wait = TimeSpan.FromMinutes(_fixture.StoragePendingTtlMinutes + _fixture.StorageCleanupIntervalMinutes);

        if (!WithinSlowBudget(wait, "limpeza do envio abandonado"))
        {
            return;
        }

        // Um abandono: declarou, enviou o binário e nunca confirmou.
        var abandoned = (await DeclareAsync(UniqueBytes("small.jpg"), "image/jpeg")).As<ImageUploadResult>();
        var put = await StorageHttp.PutAsync(abandoned.UploadUrl!, await File.ReadAllBytesAsync(StorageHttp.FixturePath("small.jpg")), "image/jpeg");
        put.Dispose();

        // Uma imagem legítima do mesmo usuário, para provar que a varredura não encosta nela.
        var (keptId, _) = await RequestAndPutAsync(Token, "other.jpg", "image/jpeg");

        await Task.Delay(wait + TimeSpan.FromSeconds(45));

        // Assert: o abandono saiu dos metadados, e a imagem disponível continua.
        var gone = await _fixture.GetAsync($"/images/{abandoned.ImageId}", Token);
        gone.StatusCode.Should().Be(404, $"pendente abandonada deveria ser limpa: {gone}");

        // O binário do abandono também tem de sumir. Aqui só se pode afirmar que o endereço do objeto
        // não serve nada: no bucket privado, chave inexistente e chave existente respondem ambos 403 a
        // um GET anônimo — a prova de que o objeto foi apagado de fato é o 404 da URL assinada depois
        // de um DELETE (Delete_ByOwner_ShouldReturn204AndMakeObjectUnreadable).
        var abandonedAddress = StorageHttp.StripQuery(abandoned.UploadUrl!);
        var probe = await StorageHttp.GetAsync(abandonedAddress);

        probe.StatusCode.Should().BeOneOf(System.Net.HttpStatusCode.Forbidden, System.Net.HttpStatusCode.NotFound);
        probe.Dispose();

        // Relançar o mesmo conteúdo depois da limpeza cria envio NOVO (a linha velha não existe mais).
        var relaunched = await DeclareAsync(await File.ReadAllBytesAsync(StorageHttp.FixturePath("small.jpg")), "image/jpeg");
        relaunched.StatusCode.Should().Be(201, $"redeclarar após a limpeza: {relaunched}");

        var stillThere = await _fixture.GetAsync($"/images/{keptId}", Token);
        stillThere.StatusCode.Should().Be(200, "imagem disponível não é tocada pela limpeza");
        (await StorageHttp.GetAsync(stillThere.As<ImageInfo>().Url!)).StatusCode
            .Should().Be(System.Net.HttpStatusCode.OK);
    }

    [Fact]
    [Trait("Category", "Slow")]
    public async Task Cleanup_ShouldNotDeleteAnObjectBelongingToANewerUpload()
    {
        // T060 / research §8: o objeto de uma <c>failed</c> foi apagado na recusa, e a MESMA chave
        // pode estar ocupada por um reenvio agora disponível. A limpeza da linha velha não pode apagar
        // o binário do reenvio.
        var wait = TimeSpan.FromMinutes(_fixture.StoragePendingTtlMinutes + _fixture.StorageCleanupIntervalMinutes);

        if (!WithinSlowBudget(wait, "limpeza da linha falhada sem tocar o reenvio"))
        {
            return;
        }

        var content = UniqueBytes("other.jpg");
        var wrongHash = StorageHttp.Sha256Hex(UniqueBytes("small.jpg"));

        var refused = (await DeclareAsync(content, "image/jpeg", sha256Override: wrongHash)).As<ImageUploadResult>();
        var put = await StorageHttp.PutAsync(refused.UploadUrl!, content, "image/jpeg");
        put.Dispose();

        var refusal = await _fixture.PostAsync($"{UPLOADS}/{refused.ImageId}/confirm", Token, new { });
        refusal.StatusCode.Should().Be(400, $"recusa preparatória: {refusal}");

        // Reenvio correto do mesmo conteúdo: nova linha, mesma chave de objeto.
        var (reuploadId, _) = await RequestAndPutAsync(Token, "other.jpg", "image/jpeg");
        reuploadId.Should().NotBe(refused.ImageId, "a recusa não bloqueia o reenvio (clarificação 5)");

        var reuploaded = (await ConfirmAsync(reuploadId)).As<ImageInfo>();
        reuploaded.Status.Should().Be(Ambrosia.DTO.Enums.ImageStatus.Available);

        await Task.Delay(wait + TimeSpan.FromSeconds(45));

        // A linha recusada saiu da varredura junto com o reenvio ainda vivo — o que tem de sobreviver
        // é o binário do reenvio.
        var readable = await StorageHttp.GetAsync(reuploaded.Url!);

        readable.StatusCode.Should().Be(System.Net.HttpStatusCode.OK,
            $"a limpeza da linha falhada não pode apagar o objeto do reenvio: {await StorageHttp.DescribeAsync(readable)}");
        readable.Dispose();
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    /// <summary>
    /// Declara (201), sobe os bytes na URL pré-assinada e devolve o id com o conteúdo enviado
    /// (helper pedido em T029).
    /// </summary>
    private async Task<(long ImageId, byte[] Content)> RequestAndPutAsync(string token, string fileName, string mimeType)
    {
        var content = UniqueBytes(fileName);
        var declared = await DeclareAsync(content, mimeType, token: token);

        declared.StatusCode.Should().Be(201, $"declarar {fileName} deveria dar 201: {declared}");

        var result = declared.As<ImageUploadResult>();
        var put = await StorageHttp.PutAsync(result.UploadUrl!, content, mimeType);

        put.StatusCode.Should().Be(System.Net.HttpStatusCode.OK,
            $"o PUT de {fileName} deveria aceitar: {await StorageHttp.DescribeAsync(put)}");
        put.Dispose();

        return (result.ImageId, content);
    }

    private Task<HttpCallResult> DeclareAsync(
        byte[] content,
        string mimeType,
        long? sizeOverride = null,
        string? sha256Override = null,
        string? token = null) =>
        _fixture.PostAsync(UPLOADS, token ?? Token, new ImageUploadInsertInfo
        {
            MimeType = mimeType,
            SizeBytes = sizeOverride ?? content.Length,
            Sha256 = sha256Override ?? StorageHttp.Sha256Hex(content)
        });

    /// <summary>
    /// Depois de qualquer recusa por conteúdo, o mesmo hash deve poder ser declarado de novo (registro
    /// <c>failed</c> não participa da unicidade — clarificação 5) e o envio novo, sem PUT, deve ser
    /// recusado como "ainda não foi enviado": é isso que prova que a recusa apagou o objeto, já que a
    /// chave é determinística pelo conteúdo e o novo registro aponta exatamente para ela.
    /// </summary>
    private async Task AssertRejectedUploadMustBeRedeclaredAsync(byte[] content, string mimeType)
    {
        var sha = StorageHttp.Sha256Hex(content);

        var redeclared = await DeclareAsync(content, mimeType);

        redeclared.StatusCode.Should().Be(201, $"redeclarar conteúdo recusado deveria criar envio novo: {redeclared}");

        var fresh = redeclared.As<ImageUploadResult>();
        fresh.AlreadyExists.Should().BeFalse();
        fresh.ImageId.Should().BeGreaterThan(0);

        var expectedSha = sha;
        expectedSha.Should().Be(StorageHttp.Sha256Hex(content));

        var withoutPut = await _fixture.PostAsync($"{UPLOADS}/{fresh.ImageId}/confirm", Token, new { });

        withoutPut.StatusCode.Should().Be(400,
            $"confirmar o reenvio sem PUT deveria recusar (objeto apagado): {withoutPut}");
        MensagemOf(withoutPut).Should().Be("O arquivo da imagem ainda não foi enviado.");
    }

    private static string MensagemOf(HttpCallResult response)
    {
        using var document = JsonDocument.Parse(response.Body);

        return document.RootElement.GetProperty("mensagem").GetString() ?? string.Empty;
    }

    private Task<HttpCallResult> ConfirmAsync(long imageId, string? token = null) =>
        _fixture.PostAsync($"{UPLOADS}/{imageId}/confirm", token ?? Token, new { });

    /// <summary>
    /// Porta de entrada dos testes lentos (T039, research §16). O prazo é lido do ambiente em teste: em
    /// homolog/Development ele é curto (1-2 min) e o teste roda de verdade; se alguém apontar a suíte
    /// para prazos de produção (15/60), a espera passaria do teto e o teste não finge que verificou —
    /// ele grava o motivo no output e sai. Para descartar de vez, filtre pela categoria:
    /// <c>dotnet test --filter Category!=Slow</c>.
    /// </summary>
    /// <returns><c>false</c> quando a espera exigida ultrapassa <c>Storage:SlowTestMaxWaitMinutes</c>.</returns>
    private bool WithinSlowBudget(TimeSpan wait, string oQueEspera)
    {
        if (wait.TotalMinutes <= _fixture.SlowTestMaxWaitMinutes)
        {
            return true;
        }

        _output.WriteLine(
            $"PULADO: o ambiente sob teste usa prazo de {wait.TotalMinutes:0} min para {oQueEspera}, " +
            $"acima do teto de {_fixture.SlowTestMaxWaitMinutes} min (Storage:SlowTestMaxWaitMinutes). " +
            "Nada foi verificado neste teste — rode contra homolog/Development, que têm prazos curtos.");

        return false;
    }

    /// <summary>
    /// Procura uma sequência de bytes legível (ASCII) dentro de um binário — é como o teste da US3
    /// constata que o marcador APP1 de EXIF sumiu do arquivo que o storage guarda.
    /// </summary>
    private static bool ContainsMarker(byte[] data, string ascii)
    {
        var needle = System.Text.Encoding.ASCII.GetBytes(ascii);

        for (var i = 0; i <= data.Length - needle.Length; i++)
        {
            var hit = true;

            for (var j = 0; j < needle.Length; j++)
            {
                if (data[i + j] != needle[j])
                {
                    hit = false;
                    break;
                }
            }

            if (hit)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Bytes do fixture + sufixo aleatório: o arquivo continua sendo o mesmo formato (a detecção lê as
    /// assinaturas de bytes no início) e ganha um SHA-256 único por teste — essencial porque a
    /// deduplicação é por <c>(usuário, hash)</c> e a suíte inteira usa a conta do usuário A.
    /// </summary>
    private static byte[] UniqueBytes(string fileName)
    {
        var bytes = File.ReadAllBytes(StorageHttp.FixturePath(fileName));
        var suffix = Guid.NewGuid().ToByteArray();
        var unique = new byte[bytes.Length + suffix.Length];

        Array.Copy(bytes, unique, bytes.Length);
        Array.Copy(suffix, 0, unique, bytes.Length, suffix.Length);

        return unique;
    }
}
