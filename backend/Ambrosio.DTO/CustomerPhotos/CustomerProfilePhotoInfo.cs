using System.Text.Json.Serialization;

namespace Ambrosio.DTO.CustomerPhotos;

/// <summary>
/// Foto de perfil da cliente, embutida em <c>CustomerInfo</c> e <c>CustomerDetailInfo</c>
/// (feature 009, data-model §6). É o que o avatar das telas lê.
/// </summary>
public class CustomerProfilePhotoInfo
{
    /// <summary>Foto do álbum de onde o perfil saiu (FR-014).</summary>
    [JsonPropertyName("photoId")]
    public long PhotoId { get; set; }

    /// <summary>
    /// Imagem exibida no círculo: o recorte do navegador quando existe, senão o arquivo da própria
    /// foto (research R4/R5).
    /// </summary>
    [JsonPropertyName("imageId")]
    public long ImageId { get; set; }

    /// <summary>Imagem da foto do álbum — o "link com a foto original" que a spec pede.</summary>
    [JsonPropertyName("originalImageId")]
    public long OriginalImageId { get; set; }

    /// <summary>URL pré-assinada da imagem do círculo.</summary>
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    /// <summary>
    /// URL pré-assinada do original, para o toque no avatar abrir a foto inteira (US3-4).
    /// <b>Ausente na listagem de clientes</b>: uma URL assinada por linha já custa, e a lista não
    /// abre visualizador (data-model §6).
    /// </summary>
    [JsonPropertyName("originalUrl")]
    public string? OriginalUrl { get; set; }

    [JsonPropertyName("urlExpiresAt")]
    public DateTime? UrlExpiresAt { get; set; }
}
