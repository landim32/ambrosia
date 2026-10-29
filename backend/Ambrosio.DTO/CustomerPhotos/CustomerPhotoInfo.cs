using System.Text.Json.Serialization;
using Ambrosio.DTO.Enums;

namespace Ambrosio.DTO.CustomerPhotos;

/// <summary>
/// Uma foto do álbum da cliente (feature 009, data-model §6). Item da grade e do visualizador.
/// <c>url</c> é a do <b>arquivo original</b> — não existe miniatura gerada no servidor
/// (research R3) — e é sempre temporária (FR-026): <see cref="UrlExpiresAt"/> diz quando vence.
/// </summary>
public class CustomerPhotoInfo
{
    [JsonPropertyName("photoId")]
    public long PhotoId { get; set; }

    [JsonPropertyName("customerId")]
    public long CustomerId { get; set; }

    /// <summary>Id da <c>stored_image</c> (feature 004): é com ele que a tela renova a URL.</summary>
    [JsonPropertyName("imageId")]
    public long ImageId { get; set; }

    /// <summary>Legenda opcional, até 500 caracteres; <c>null</c> quando vazia (FR-007).</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("isFavorite")]
    public bool IsFavorite { get; set; }

    /// <summary>É a foto de onde saiu a foto de perfil desta cliente (FR-011).</summary>
    [JsonPropertyName("isProfile")]
    public bool IsProfile { get; set; }

    [JsonPropertyName("source")]
    public CustomerPhotoSource Source { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; }

    /// <summary>URL pré-assinada de leitura. <c>null</c> só em situação excepcional.</summary>
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("urlExpiresAt")]
    public DateTime? UrlExpiresAt { get; set; }
}
