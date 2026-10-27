namespace Ambrosia.DTO.Images;

/// <summary>
/// Resultado de olhar o cabeçalho de um binário sem decodificá-lo inteiro (research §7): o tipo
/// real pelo conteúdo (não pelo nome nem pelo cabeçalho declarado) e o tamanho em pixels.
/// </summary>
public class ImageInspection
{
    /// <summary>Mime detectado pelas assinaturas de bytes: <c>image/jpeg</c>, <c>image/png</c>, <c>image/webp</c>.</summary>
    public string MimeType { get; set; } = string.Empty;

    /// <summary>Largura em pixels; <c>null</c> quando o formato não expõe a dimensão no cabeçalho.</summary>
    public int? Width { get; set; }

    /// <summary>Altura em pixels; mesmo contrato de <see cref="Width"/>.</summary>
    public int? Height { get; set; }
}
