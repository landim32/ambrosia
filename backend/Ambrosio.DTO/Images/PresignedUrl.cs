namespace Ambrosio.DTO.Images;

/// <summary>
/// URL pré-assinada gerada pela Infra (research §4, §5) e seu prazo de validade, para que o Domain
/// devolva <c>urlExpiresAt</c> ao cliente sem conhecer o SDK.
/// </summary>
public class PresignedUrl
{
    /// <summary>URL assinada (SigV4) — temporária, nunca persistida nem logada (FR-013).</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Instante UTC em que a URL deixa de ser aceita pelo storage.</summary>
    public DateTime ExpiresAt { get; set; }
}
