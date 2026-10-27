using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Metadata;
using SixLabors.ImageSharp.Processing;
using Ambrosia.DTO.Images;
using Ambrosia.Infra.Interfaces.AppServices;

namespace Ambrosia.Infra.AppServices;

/// <summary>
/// Inspeção e sanitização de imagens com ImageSharp (research §7). Gerenciado, sem binário nativo no
/// container Linux, e atrás de <see cref="IImageProcessingAppService"/> para que o Domain não conheça
/// a biblioteca.
/// <para>
/// <b>Tipo pelo conteúdo</b>: <see cref="Image.DetectFormatAsync(Stream, CancellationToken)"/> lê as
/// assinaturas de bytes, então um PDF renomeado <c>.jpg</c> não passa (FR-005, US3 cenário 3). As
/// dimensões vêm de <see cref="Image.IdentifyAsync(Stream, CancellationToken)"/>, que só olha o
/// cabeçalho — não decodifica um arquivo de 10 MB para saber que ele tem 4000x3000.
/// </para>
/// <para>
/// <b>Sanitização</b> (FR-010): <c>AutoOrient</c> aplica a rotação da tag EXIF para a foto não ficar
/// deitada depois que a tag é removida; os perfis EXIF, IPTC e XMP são zerados no frame raiz e em cada
/// frame (é onde mora o GPS de uma foto de celular); o perfil ICC fica, para não alterar as cores. A
/// re-codificação no formato de origem ainda neutraliza arquivos "poliglotas" — imagem válida com
/// payload extra appended, que sobreviveria a uma simples cópia de bytes.
/// </para>
/// </summary>
public class ImageProcessingAppService : IImageProcessingAppService
{
    /// <summary>Qualidade JPEG/WebP da re-codificação: visualmente igual, arquivo menor.</summary>
    private const int RECODE_QUALITY = 90;

    /// <inheritdoc />
    public async Task<ImageInspection?> InspectAsync(Stream content, CancellationToken ct = default)
    {
        var buffer = await BufferAsync(content, ct);

        var format = await Image.DetectFormatAsync(buffer, ct);

        if (format is null)
        {
            return null;
        }

        buffer.Position = 0;

        try
        {
            var info = await Image.IdentifyAsync(buffer, ct);

            return new ImageInspection
            {
                MimeType = format.DefaultMimeType,
                Width = info?.Width,
                Height = info?.Height
            };
        }
        catch (UnknownImageFormatException)
        {
            // Assinatura de formato conhecida, cabeçalho que não decodifica: não é imagem útil.
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<ProcessedImage> SanitizeAsync(Stream content, CancellationToken ct = default)
    {
        var buffer = await BufferAsync(content, ct);

        using var image = await Image.LoadAsync(buffer, ct);

        image.Mutate(context => context.AutoOrient());

        var format = image.Metadata.DecodedImageFormat
            ?? throw new UnknownImageFormatException("A imagem decodificada não reporta formato de origem.");

        // EXIF/IPTC/XMP vão todos embora; o ICC fica (research §7).
        ClearPrivacyProfiles(image.Metadata);

        foreach (var frame in image.Frames)
        {
            ClearPrivacyProfiles(frame.Metadata);
        }

        using var output = new MemoryStream();
        await image.SaveAsync(output, BuildEncoder(format), ct);

        return new ProcessedImage
        {
            Content = output.ToArray(),
            MimeType = format.DefaultMimeType,
            // Dimensões DEPOIS da orientação: é o que o usuário vai ver (data-model, width/height).
            Width = image.Width,
            Height = image.Height
        };
    }

    private static void ClearPrivacyProfiles(ImageMetadata metadata)
    {
        metadata.ExifProfile = null;
        metadata.IptcProfile = null;
        metadata.XmpProfile = null;
    }

    /// <summary>
    /// Mesma limpeza no frame: em PNG/APNG e GIF o metadado de privacidade pode estar no frame e não
    /// no raiz. <see cref="ImageFrameMetadata"/> não compartilha tipo com <see cref="ImageMetadata"/>,
    /// por isso os dois métodos.
    /// </summary>
    private static void ClearPrivacyProfiles(ImageFrameMetadata metadata)
    {
        metadata.ExifProfile = null;
        metadata.IptcProfile = null;
        metadata.XmpProfile = null;
    }

    /// <summary>
    /// Re-codifica no mesmo formato da entrada (research §7). Um formato que passa na detecção mas não
    /// tem encoder aqui é unsupported: recusa-se o conteúdo, nunca se grava binário não validado.
    /// </summary>
    private static IImageEncoder BuildEncoder(IImageFormat format) => format switch
    {
        JpegFormat => new JpegEncoder { Quality = RECODE_QUALITY },
        PngFormat => new PngEncoder(),
        WebpFormat => new WebpEncoder { Quality = RECODE_QUALITY },
        _ => throw new UnknownImageFormatException(
            "O formato detectado não tem re-codificação disponível.")
    };

    /// <summary>
    /// Cópia em buffer próprio: os dois passes (formato e cabeçalho, ou decodificar e re-codificar)
    /// precisam reler do início, e o stream do storage é de rede — não seekable. Devolver o stream do
    /// chamador para um <c>using</c> interno também o descartaria por cima dele, então o buffer é sempre
    /// nosso (no máximo <c>MaxUploadBytes</c>, limitado antes de chegar aqui).
    /// </summary>
    private static async Task<MemoryStream> BufferAsync(Stream content, CancellationToken ct)
    {
        if (content.CanSeek)
        {
            content.Position = 0;
        }

        var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        buffer.Position = 0;

        return buffer;
    }
}
