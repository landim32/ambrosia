using Ambrosia.DTO.Images;

namespace Ambrosia.Infra.Interfaces.AppServices;

/// <summary>
/// Inspeção e sanitização de imagens (research §7), atrás de uma interface para que o Domain não
/// conheça ImageSharp nem a troca de biblioteca afete as regras de negócio.
/// </summary>
public interface IImageProcessingAppService
{
    /// <summary>
    /// Lê o <b>formato real</b> pelas assinaturas de bytes e as dimensões pelo cabeçalho, sem
    /// decodificar a imagem inteira (FR-005, FR-006). Um PDF renomeado <c>.jpg</c> cai aqui.
    /// </summary>
    /// <returns>Mime + dimensões; <c>null</c> quando o formato não é reconhecido.</returns>
    Task<ImageInspection?> InspectAsync(Stream content, CancellationToken ct = default);

    /// <summary>
    /// Decodifica, corrige a orientação pela tag EXIF, remove os perfis EXIF, IPTC e XMP (mantendo o
    /// ICC para não alterar as cores) e re-codifica no mesmo formato (FR-010, research §7). A
    /// re-codificação também neutraliza arquivos "poliglotas" — imagem válida com conteúdo extra.
    /// </summary>
    /// <returns>Bytes finais com as dimensões já pós-orientação.</returns>
    Task<ProcessedImage> SanitizeAsync(Stream content, CancellationToken ct = default);
}
