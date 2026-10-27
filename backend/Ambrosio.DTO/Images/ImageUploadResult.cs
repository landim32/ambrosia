using System.Text.Json.Serialization;

namespace Ambrosio.DTO.Images;

/// <summary>
/// Resposta de <c>POST /images/uploads</c> (contrato <c>images-api.md</c>). Ou o cliente recebe a
/// URL pré-assinada onde fazer o <c>PUT</c> (<see cref="AlreadyExists"/> = false), ou a imagem já
/// disponível é devolvida na hora, sem nenhum envio (FR-011, SC-005).
/// <para>
/// <see cref="UploadUrl"/> é assinada e temporária: nunca é persistida nem logada (FR-013).
/// </para>
/// </summary>
public class ImageUploadResult
{
    /// <summary>Identificador da imagem (nova, reaproveitada ou já disponível).</summary>
    [JsonPropertyName("imageId")]
    public long ImageId { get; set; }

    /// <summary>Já existia imagem <c>available</c> deste conteúdo para este usuário.</summary>
    [JsonPropertyName("alreadyExists")]
    public bool AlreadyExists { get; set; }

    /// <summary>URL pré-assinada de envio; <c>null</c> quando <see cref="AlreadyExists"/>.</summary>
    [JsonPropertyName("uploadUrl")]
    public string? UploadUrl { get; set; }

    /// <summary>Verbo a usar na <see cref="UploadUrl"/> — sempre <c>PUT</c> (research §4).</summary>
    [JsonPropertyName("uploadMethod")]
    public string? UploadMethod { get; set; }

    /// <summary>
    /// Valor exato do cabeçalho <c>Content-Type</c> a enviar. Está assinado: um <c>PUT</c> com outro
    /// tipo é recusado pelo storage com 403 (FR-003, US1 cenário 2).
    /// </summary>
    [JsonPropertyName("contentType")]
    public string? ContentType { get; set; }

    /// <summary>Instante UTC em que a <see cref="UploadUrl"/> deixa de valer.</summary>
    [JsonPropertyName("uploadExpiresAt")]
    public DateTime? UploadExpiresAt { get; set; }

    /// <summary>Imagem disponível (deduplicação); <c>null</c> quando há envio a fazer.</summary>
    [JsonPropertyName("image")]
    public ImageInfo? Image { get; set; }
}
