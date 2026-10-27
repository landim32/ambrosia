using System.Text.Json.Serialization;
using Ambrosia.DTO.Enums;

namespace Ambrosia.DTO.Images;

/// <summary>
/// Imagem como a API a apresenta (contrato <c>images-api.md</c>): saída de confirmar envio, gravar
/// pelo servidor e consultar. <see cref="Url"/> é uma pré-assinatura de leitura gerada <b>a cada
/// resposta</b> e só existe quando o status é <see cref="ImageStatus.Available"/> (FR-012, FR-015).
/// <para>
/// Não expõe <c>userId</c> nem a chave do objeto: a chave é detalhe interno e o dono vem do token
/// (FR-014). As datas são UTC, como as colunas <c>timestamp without time zone</c>.
/// </para>
/// </summary>
public class ImageInfo
{
    [JsonPropertyName("imageId")]
    public long ImageId { get; set; }

    [JsonPropertyName("mimeType")]
    public string MimeType { get; set; } = string.Empty;

    [JsonPropertyName("sizeBytes")]
    public long SizeBytes { get; set; }

    /// <summary>SHA-256 em hex minúsculo (64 chars) do binário que está no storage.</summary>
    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>Largura em pixels; <c>null</c> enquanto pendente ou se o cabeçalho não permitiu ler.</summary>
    [JsonPropertyName("width")]
    public int? Width { get; set; }

    /// <summary>Altura em pixels; mesmo contrato de <see cref="Width"/>.</summary>
    [JsonPropertyName("height")]
    public int? Height { get; set; }

    [JsonPropertyName("status")]
    public ImageStatus Status { get; set; }

    [JsonPropertyName("source")]
    public ImageSource Source { get; set; }

    /// <summary>URL pré-assinada de leitura; <c>null</c> fora de <c>available</c> (FR-015).</summary>
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    /// <summary>Instante UTC em que <see cref="Url"/> expira.</summary>
    [JsonPropertyName("urlExpiresAt")]
    public DateTime? UrlExpiresAt { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }
}
