using System.Text.Json.Serialization;

namespace Ambrosio.DTO.CustomerPhotos;

/// <summary>
/// Query string de <c>GET /customers/{customerId}/photos</c> (feature 009, contracts/api.md §1).
/// </summary>
public class CustomerPhotoListQuery
{
    /// <summary>Id da última foto da página anterior; <c>null</c> = primeira página.</summary>
    [JsonPropertyName("cursor")]
    public long? Cursor { get; set; }

    /// <summary>Tamanho da página (1 a 60). Padrão 24: três linhas de três colunas na grade.</summary>
    [JsonPropertyName("pageSize")]
    public int PageSize { get; set; } = 24;

    /// <summary><c>true</c> = só as favoritas (FR-010, cenário US4-6).</summary>
    [JsonPropertyName("favorites")]
    public bool Favorites { get; set; }
}
