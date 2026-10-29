using System.Text.Json.Serialization;
using Ambrosio.DTO.CustomerPhotos;

namespace Ambrosio.DTO.Customers;

/// <summary>
/// Cliente em listagem (FR-008). Traz apenas <see cref="CpfMasked"/>: o documento completo só
/// aparece no detalhe (SC-005).
/// </summary>
public class CustomerInfo
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>CPF mascarado: <c>***.982.247-**</c> (dígitos 4 a 9 visíveis).</summary>
    [JsonPropertyName("cpfMasked")]
    public string CpfMasked { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Foto de perfil da cliente (feature 009, FR-016): é o que o avatar redondo lê no lugar das
    /// iniciais. <c>null</c> quando ela não tem perfil — e aí as iniciais continuam (cenário US3-5).
    /// <para>
    /// Na <b>listagem</b> vem sem <c>originalUrl</c>: assinar a URL do original custa uma assinatura
    /// por linha e nenhuma lista abre visualizador (data-model §6).
    /// </para>
    /// </summary>
    [JsonPropertyName("profilePhoto")]
    public CustomerProfilePhotoInfo? ProfilePhoto { get; set; }
}
