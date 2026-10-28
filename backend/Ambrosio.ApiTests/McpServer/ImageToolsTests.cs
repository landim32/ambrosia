using System.Text.Json;
using Ambrosio.ApiTests.Fixtures;
using Ambrosio.ApiTests.Helpers;
using FluentAssertions;

namespace Ambrosio.ApiTests.McpServer;

/// <summary>
/// T063 [US5]: imagens pelo MCP. O arquivo da conversa chega como <c>download_url</c> https (aqui, a
/// URL pré-assinada de uma imagem guardada pela API, servida pelo storage do ambiente); download de
/// http, localhost ou IP privado é recusado; tipos fora da lista têm a mesma recusa da API.
/// </summary>
[Collection(ApiTestCollection.API_TESTS)]
public class ImageToolsTests(ApiTestFixture fixture)
{
    private const string ImageScopes = "openid email profile customers:read images:write";

    [Fact]
    public async Task EnviarImagem_FromHttpsUrl_PreviewsStoresAndDeduplicates()
    {
        var url = await PublicImageUrlAsync();
        var seller = await McpSeller.CreateAsync(fixture, scopes: ImageScopes);
        var arquivo = new { download_url = url, mime_type = "image/jpeg", file_name = "foto.jpg" };

        var preview = WriteToolsTests.Structured(await seller.CallAsync("enviar_imagem", new { arquivo }));
        preview.GetProperty("gravado").GetBoolean().Should().BeFalse();
        preview.GetProperty("previa").GetProperty("tipo").GetString().Should().Be("image/jpeg");

        var first = WriteToolsTests.Structured(await seller.CallAsync("enviar_imagem", new { arquivo, confirmar = true }));
        first.GetProperty("registro").GetProperty("criada").GetBoolean().Should().BeTrue();
        var imageId = first.GetProperty("registro").GetProperty("imagem").GetProperty("imagemId").GetInt64();

        var second = WriteToolsTests.Structured(await seller.CallAsync("enviar_imagem", new { arquivo, confirmar = true }));
        second.GetProperty("registro").GetProperty("criada").GetBoolean().Should().BeFalse();
        second.GetProperty("registro").GetProperty("imagem").GetProperty("imagemId").GetInt64().Should().Be(imageId);

        var read = WriteToolsTests.Structured(await seller.CallAsync("obter_imagem", new { imagem_id = imageId }));
        read.GetProperty("url").GetString().Should().StartWith("https://");
    }

    [Fact]
    public async Task EnviarImagem_WithTypeOutsideTheList_ReturnsTheApiRefusal()
    {
        var url = await PublicImageUrlAsync();
        var seller = await McpSeller.CreateAsync(fixture, scopes: ImageScopes);

        var result = await seller.CallAsync("enviar_imagem", new { arquivo = new { download_url = url, mime_type = "image/gif" }, confirmar = true });

        result.IsToolError.Should().BeTrue();
        result.Text.Should().Be("O conteúdo do arquivo não é uma imagem de tipo aceito.");
    }

    [Theory]
    [InlineData("http://example.com/foto.jpg")]
    [InlineData("https://localhost/foto.jpg")]
    [InlineData("https://127.0.0.1/foto.jpg")]
    [InlineData("https://10.0.0.5/foto.jpg")]
    [InlineData("https://169.254.169.254/latest/meta-data")]
    public async Task EnviarImagem_FromNonPublicOrNonHttpsUrl_IsRefused(string url)
    {
        var seller = await McpSeller.CreateAsync(fixture, scopes: ImageScopes);

        var result = await seller.CallAsync("enviar_imagem", new { arquivo = new { download_url = url, mime_type = "image/jpeg" }, confirmar = true });

        result.IsToolError.Should().BeTrue();
        result.Text.Should().Contain("download_url");
    }

    [Fact]
    public async Task SolicitarPutConfirmar_UploadFlowWorks()
    {
        var seller = await McpSeller.CreateAsync(fixture, scopes: ImageScopes);
        var bytes = UniqueBytes("small.png");
        var sha = StorageHttp.Sha256Hex(bytes);

        var request = WriteToolsTests.Structured(await seller.CallAsync("solicitar_upload_imagem", new
        {
            tipo_mime = "image/png",
            tamanho_bytes = bytes.Length,
            sha256 = sha,
            confirmar = true
        })).GetProperty("registro");

        var imageId = request.GetProperty("imagemId").GetInt64();
        var put = await StorageHttp.PutAsync(request.GetProperty("urlEnvio").GetString()!, bytes, "image/png");
        put.IsSuccessStatusCode.Should().BeTrue(await StorageHttp.DescribeAsync(put));

        var confirmed = WriteToolsTests.Structured(await seller.CallAsync("confirmar_upload_imagem", new { imagem_id = imageId, confirmar = true }));
        confirmed.GetProperty("registro").GetProperty("situacao").GetString().Should().Be("available");
    }

    [Fact]
    public async Task ExcluirImagem_PreviewKeeps_ConfirmDeletes()
    {
        var url = await PublicImageUrlAsync();
        var seller = await McpSeller.CreateAsync(fixture, scopes: ImageScopes);
        var stored = WriteToolsTests.Structured(await seller.CallAsync("enviar_imagem", new
        {
            arquivo = new { download_url = url, mime_type = "image/jpeg" },
            confirmar = true
        }));
        var imageId = stored.GetProperty("registro").GetProperty("imagem").GetProperty("imagemId").GetInt64();

        WriteToolsTests.Structured(await seller.CallAsync("excluir_imagem", new { imagem_id = imageId }));
        (await seller.CallAsync("obter_imagem", new { imagem_id = imageId })).IsToolError.Should().BeFalse();

        WriteToolsTests.Structured(await seller.CallAsync("excluir_imagem", new { imagem_id = imageId, confirmar = true }));
        (await seller.CallAsync("obter_imagem", new { imagem_id = imageId })).IsToolError.Should().BeTrue();
    }

    /// <summary>
    /// Guarda uma imagem única pela API (conta própria) e devolve a URL pré-assinada de leitura: um
    /// arquivo https público, como o <c>download_url</c> que o ChatGPT entrega.
    /// </summary>
    private async Task<string> PublicImageUrlAsync()
    {
        var owner = await McpTestUser.CreateAsync(fixture, "Dona da Imagem");
        var upload = await fixture.PostFileAsync("/images", owner.ApiToken, UniqueBytes("small.jpg"), "image/jpeg", "small.jpg");
        upload.IsSuccess.Should().BeTrue(upload.Body);

        using var created = JsonDocument.Parse(upload.Body);
        var imageId = created.RootElement.GetProperty("imageId").GetInt64();

        var read = await fixture.GetAsync($"/images/{imageId}", owner.ApiToken);
        using var image = JsonDocument.Parse(read.Body);

        return image.RootElement.GetProperty("url").GetString()!;
    }

    private static byte[] UniqueBytes(string fileName)
    {
        var bytes = File.ReadAllBytes(StorageHttp.FixturePath(fileName));
        var suffix = Guid.NewGuid().ToByteArray();

        return [.. bytes, .. suffix];
    }
}
