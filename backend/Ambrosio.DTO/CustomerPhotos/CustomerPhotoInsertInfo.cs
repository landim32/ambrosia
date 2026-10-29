using System.Text.Json.Serialization;
using Ambrosio.DTO.Enums;

namespace Ambrosio.DTO.CustomerPhotos;

/// <summary>
/// Corpo de <c>POST /customers/{customerId}/photos</c> (feature 009, contracts/api.md §2).
/// Nenhum byte de arquivo aqui: os bytes entram pelas rotas de imagem da feature 004 e este corpo
/// apenas anexa o <see cref="ImageId"/>resultante ao álbum (research R7).
/// </summary>
public class CustomerPhotoInsertInfo
{
    /// <summary>Imagem já gravada e <c>available</c>, do próprio usuário.</summary>
    [JsonPropertyName("imageId")]
    public long ImageId { get; set; }

    /// <summary>Legenda opcional (até 500 caracteres). O arraste da tela não envia nada (FR-020).</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Origem declarada: só <c>web</c> ou <c>api</c>; ausente = <c>api</c>. <c>chatGpt</c> é recusado
    /// pelo validator, para a conversa ter a origem própria (research R12).
    /// </summary>
    [JsonPropertyName("source")]
    public CustomerPhotoSource? Source { get; set; }
}
