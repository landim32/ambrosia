namespace Ambrosia.DTO.Images;

/// <summary>
/// Binário já re-codificado pelo processador do servidor (research §7): orientação corrigida, sem
/// EXIF/IPTC/XMP, no mesmo formato de entrada. É o conteúdo que vai ao storage e sobre o qual o
/// SHA-256 da deduplicação é calculado (FR-010).
/// </summary>
public class ProcessedImage
{
    /// <summary>Bytes finais, prontos para gravar.</summary>
    public byte[] Content { get; set; } = [];

    /// <summary>Mime do formato de saída (igual ao de entrada).</summary>
    public string MimeType { get; set; } = string.Empty;

    /// <summary>Largura após <c>AutoOrient</c> — a dimensão real da foto que será vista.</summary>
    public int Width { get; set; }

    /// <summary>Altura após <c>AutoOrient</c>.</summary>
    public int Height { get; set; }
}
