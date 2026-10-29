using System.Text.Json.Serialization;

namespace Ambrosio.DTO.CustomerPhotos;

/// <summary>
/// Corpo de <c>PUT /customers/{customerId}/profile-photo</c> (feature 009, FR-011/FR-012,
/// contracts/api.md §6).
/// </summary>
public class ProfilePhotoUpdateInfo
{
    /// <summary>Foto do álbum de onde o perfil saiu (obrigatório; FR-014).</summary>
    [JsonPropertyName("photoId")]
    public long PhotoId { get; set; }

    /// <summary>
    /// Recorte quadrado de até 512 × 512 <b>gerado no navegador</b> e já gravado como imagem
    /// (research R4). Opcional: sem ele o círculo usa o arquivo da própria foto — é o caso da
    /// conversa no ChatGPT, onde não há navegador para recortar.
    /// </summary>
    [JsonPropertyName("imageId")]
    public long? ImageId { get; set; }
}
