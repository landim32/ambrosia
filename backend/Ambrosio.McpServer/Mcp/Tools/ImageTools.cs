using System.ComponentModel;
using System.Globalization;
using System.Text.Json.Serialization;
using Ambrosio.Domain.Exceptions;
using Ambrosio.Domain.Interfaces;
using Ambrosio.DTO.Enums;
using Ambrosio.DTO.Images;
using Ambrosio.DTO.Settings;
using Ambrosio.McpServer.Mcp.Attributes;
using Ambrosio.McpServer.Mcp.Files;
using Ambrosio.McpServer.Mcp.Results;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Ambrosio.McpServer.Mcp.Tools;

/// <summary>Arquivo da conversa que o ChatGPT entrega à tool: um link de download, nunca base64.</summary>
public sealed class FileArgument
{
    [JsonPropertyName("download_url")]
    [Description("URL https de onde o arquivo é baixado (o ChatGPT fornece).")]
    public string? DownloadUrl { get; set; }

    [JsonPropertyName("mime_type")]
    [Description("Tipo do arquivo (ex.: image/jpeg).")]
    public string? MimeType { get; set; }

    [JsonPropertyName("file_name")]
    [Description("Nome do arquivo (ex.: foto.jpg).")]
    public string? FileName { get; set; }
}

/// <summary>
/// Imagens (feature 007, T067): as cinco rotas de <c>/images</c>, pelo mesmo <see cref="IImageService"/>
/// do controller. As escritas seguem o padrão <c>confirmar</c> e são auditadas.
/// </summary>
[McpServerToolType]
public sealed class ImageTools
{
    [McpServerTool(Name = "obter_imagem", Title = "Ver imagem", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description(
        "Mostra os dados de uma imagem guardada no Ambrosio e um link temporário para abri-la (o link " +
        "expira em minutos; peça de novo se expirar).")]
    [AmbrosioScopes("customers:read")]
    [AmbrosioEndpoint("GET /images/{imageId}")]
    public static Task<CallToolResult> ObterImagem(
        McpUserContext user,
        IImageService images,
        [Description("Id da imagem no Ambrosio.")] long imagem_id) =>
        ToolResultFactory.GuardAsync(async () =>
        {
            var image = await images.GetByIdAsync(user.RequireUserId(), imagem_id);

            return ToolResultFactory.Success(Map(image), $"Imagem {image.ImageId}: {image.MimeType}, {Kb(image.SizeBytes)}.");
        });

    [McpServerTool(Name = "enviar_imagem", Title = "Guardar imagem da conversa", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description(
        "Guarda no Ambrosio uma imagem enviada na conversa (JPEG, PNG ou WebP, até o limite do " +
        "armazenamento). Recebe o arquivo como link de download, nunca como base64. Chame primeiro com " +
        "confirmar=false para ver tipo e tamanho sem gravar; com confirmar=true grava. A mesma imagem " +
        "enviada de novo não duplica (criada=false).")]
    [AmbrosioScopes("images:write")]
    [AmbrosioEndpoint("POST /images")]
    public static Task<CallToolResult> EnviarImagem(
        McpUserContext user,
        IImageService images,
        SafeFileDownloader downloader,
        IOptions<ObjectStorageSettings> storage,
        WriteToolHelper writer,
        [Description("Arquivo da conversa: download_url (https), mime_type e file_name.")] FileArgument arquivo,
        [Description("false = só prévia, nada gravado; true = grava (depois do sim da vendedora).")] bool confirmar = false,
        CancellationToken cancellationToken = default)
    {
        return writer.RunAsync(
            confirmar,
            preview: async () =>
            {
                var file = await downloader.DownloadAsync(arquivo?.DownloadUrl, arquivo?.MimeType, arquivo?.FileName, cancellationToken);
                var mime = file.MimeType.Split(';')[0].Trim().ToLowerInvariant();

                // Mesmas recusas do ImageService, com as mesmas mensagens, sem gravar nada.
                if (!storage.Value.AllowedMimeTypes.Contains(mime, StringComparer.OrdinalIgnoreCase))
                {
                    throw new ImageRejectedException(ImageRejectionReason.Type);
                }

                if (file.Content.Length == 0)
                {
                    throw new ImageRejectedException(ImageRejectionReason.Size);
                }

                return (
                    new { tipo = mime, tamanhoBytes = file.Content.Length, nome = file.FileName },
                    $"Guardar {file.FileName ?? "a imagem"} ({mime}, {Kb(file.Content.Length)}).");
            },
            execute: async () =>
            {
                var file = await downloader.DownloadAsync(arquivo?.DownloadUrl, arquivo?.MimeType, arquivo?.FileName, cancellationToken);
                using var content = new MemoryStream(file.Content);

                var (image, created) = await images.StoreFromServerAsync(user.RequireUserId(), content, file.MimeType, ImageSource.ChatGpt);

                return (
                    new { imagem = Map(image), criada = created },
                    created ? $"Imagem guardada (id {image.ImageId})." : $"Essa imagem já estava guardada (id {image.ImageId}).");
            });
    }

    [McpServerTool(Name = "solicitar_upload_imagem", Title = "Preparar envio de imagem", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description(
        "Prepara o envio direto de uma imagem: devolve um link temporário para enviar o arquivo por PUT " +
        "e o id da imagem, a ser confirmado com confirmar_upload_imagem. Só quando quem envia é um " +
        "cliente capaz de fazer o PUT; para imagens da conversa use enviar_imagem.")]
    [AmbrosioScopes("images:write")]
    [AmbrosioEndpoint("POST /images/uploads")]
    public static Task<CallToolResult> SolicitarUploadImagem(
        McpUserContext user,
        IImageService images,
        IOptions<ObjectStorageSettings> storage,
        WriteToolHelper writer,
        [Description("Tipo do arquivo: image/jpeg, image/png ou image/webp.")] string tipo_mime,
        [Description("Tamanho exato do arquivo em bytes.")] long tamanho_bytes,
        [Description("SHA-256 do arquivo, em hexadecimal (64 caracteres).")] string sha256,
        [Description("false = só prévia, nada gravado; true = prepara o envio.")] bool confirmar = false)
    {
        var info = new ImageUploadInsertInfo { MimeType = tipo_mime, SizeBytes = tamanho_bytes, Sha256 = sha256 };

        return writer.RunAsync(
            confirmar,
            preview: async () =>
            {
                await writer.ValidateAsync(info);

                return (new { tipo = tipo_mime, tamanhoBytes = tamanho_bytes }, $"Preparar envio de {tipo_mime} ({Kb(tamanho_bytes)}).");
            },
            execute: async () =>
            {
                var (result, created) = await images.RequestUploadAsync(user.RequireUserId(), info);

                return (
                    new
                    {
                        imagemId = result.ImageId,
                        jaExiste = result.AlreadyExists,
                        urlEnvio = result.UploadUrl,
                        metodo = result.UploadMethod,
                        contentType = result.ContentType,
                        urlExpiraEm = result.UploadExpiresAt?.ToString("O", CultureInfo.InvariantCulture),
                        criada = created
                    },
                    result.AlreadyExists
                        ? $"Essa imagem já está guardada (id {result.ImageId})."
                        : $"Envio preparado (id {result.ImageId}): faça o PUT no link e confirme com confirmar_upload_imagem.");
            });
    }

    [McpServerTool(Name = "confirmar_upload_imagem", Title = "Confirmar envio de imagem", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description(
        "Confirma um envio preparado por solicitar_upload_imagem depois do PUT do arquivo: o Ambrosio " +
        "confere tipo, tamanho e hash e libera a imagem, ou explica a recusa.")]
    [AmbrosioScopes("images:write")]
    [AmbrosioEndpoint("POST /images/uploads/{imageId}/confirm")]
    public static Task<CallToolResult> ConfirmarUploadImagem(
        McpUserContext user,
        IImageService images,
        WriteToolHelper writer,
        [Description("Id devolvido por solicitar_upload_imagem.")] long imagem_id,
        [Description("false = só prévia, nada gravado; true = confirma.")] bool confirmar = false)
    {
        return writer.RunAsync(
            confirmar,
            preview: async () =>
            {
                var image = await images.GetByIdAsync(user.RequireUserId(), imagem_id);

                return (Map(image), $"Confirmar o envio da imagem {imagem_id} (situação atual: {image.Status.ToString().ToLowerInvariant()}).");
            },
            execute: async () =>
            {
                var image = await images.ConfirmUploadAsync(user.RequireUserId(), imagem_id);

                return (Map(image), $"Imagem {image.ImageId} disponível.");
            });
    }

    [McpServerTool(Name = "excluir_imagem", Title = "Excluir imagem", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "Exclui uma imagem do Ambrosio. Chame primeiro com confirmar=false para ver qual imagem será " +
        "excluída e só com confirmar=true depois do sim da vendedora.")]
    [AmbrosioScopes("images:write")]
    [AmbrosioEndpoint("DELETE /images/{imageId}")]
    public static Task<CallToolResult> ExcluirImagem(
        McpUserContext user,
        IImageService images,
        WriteToolHelper writer,
        [Description("Id da imagem no Ambrosio.")] long imagem_id,
        [Description("false = só prévia, nada excluído; true = exclui.")] bool confirmar = false)
    {
        return writer.RunAsync(
            confirmar,
            preview: async () =>
            {
                var image = await images.GetByIdAsync(user.RequireUserId(), imagem_id);

                return (Map(image), $"Excluir a imagem {image.ImageId} ({image.MimeType}, {Kb(image.SizeBytes)}).");
            },
            execute: async () =>
            {
                await images.DeleteAsync(user.RequireUserId(), imagem_id);

                return (new { imagemId = imagem_id, excluida = true }, $"Imagem {imagem_id} excluída.");
            });
    }

    private static object Map(ImageInfo image) => new
    {
        imagemId = image.ImageId,
        tipo = image.MimeType,
        tamanhoBytes = image.SizeBytes,
        largura = image.Width,
        altura = image.Height,
        situacao = image.Status.ToString().ToLowerInvariant(),
        origem = image.Source.ToString().ToLowerInvariant(),
        url = image.Url,
        urlExpiraEm = image.UrlExpiresAt?.ToString("O", CultureInfo.InvariantCulture),
        criadaEm = image.CreatedAt.ToString("O", CultureInfo.InvariantCulture)
    };

    private static string Kb(long bytes) => $"{Math.Max(1, bytes / 1024)} KB";
}
