using System.Text.Json.Serialization;

namespace Ambrosia.DTO.Images;

/// <summary>
/// Declaração de envio pelo cliente (entrada de <c>POST /images/uploads</c>; contrato
/// <c>images-api.md</c>). O cliente calcula o SHA-256 do arquivo local e declara tamanho e tipo
/// <b>antes</b> de subir o binário direto ao storage — nada aqui transporta o dono do registro:
/// o <c>userId</c> vem só do claim <c>sub</c> (constituição §VI, FR-014).
/// </summary>
public class ImageUploadInsertInfo
{
    /// <summary>Tipo declarado, uma das entradas de <c>ObjectStorage:AllowedMimeTypes</c>.</summary>
    [JsonPropertyName("mimeType")]
    public string MimeType { get; set; } = string.Empty;

    /// <summary>Tamanho do binário em bytes (1 a <c>MaxUploadBytes</c>).</summary>
    [JsonPropertyName("sizeBytes")]
    public long SizeBytes { get; set; }

    /// <summary>SHA-256 do arquivo em hex (64 caracteres, maiúsculas ou minúsculas).</summary>
    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = string.Empty;
}
