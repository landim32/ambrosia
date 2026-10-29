using System.Text.Json.Serialization;

namespace Ambrosio.DTO.CustomerPhotos;

/// <summary>
/// Corpo de <c>PUT /customers/{customerId}/photos/{photoId}/description</c> (feature 009, FR-007).
/// Enviar <c>null</c> ou texto só com espaços <b>apaga</b> a descrição (cenário US4-4) — por isso o
/// campo não tem valor "sem mudança".
/// </summary>
public class CustomerPhotoDescriptionInfo
{
    [JsonPropertyName("description")]
    public string? Description { get; set; }
}
