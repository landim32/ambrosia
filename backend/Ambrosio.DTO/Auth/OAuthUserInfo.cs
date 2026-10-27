using System.Text.Json.Serialization;

namespace Ambrosio.DTO.Auth;

/// <summary>
/// Resultado da validação de um ID token externo (research §1). Produzido por
/// <c>IOAuthTokenAppService</c> (Infra) e consumido pelo <c>AuthService</c> (Domain), que nunca
/// conhece a biblioteca do provedor.
/// </summary>
public class OAuthUserInfo
{
    /// <summary>E-mail declarado pelo provedor (ainda não normalizado).</summary>
    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    /// <summary>Claim <c>email_verified</c>: obrigatório para criar/reutilizar o usuário (FR-001).</summary>
    [JsonPropertyName("emailVerified")]
    public bool EmailVerified { get; set; }

    /// <summary>Nome do provedor; pode vir vazio — o serviço usa a parte local do e-mail.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Claim <c>sub</c> do provedor, gravado em <c>users.provider_subject</c>.</summary>
    [JsonPropertyName("subject")]
    public string Subject { get; set; } = string.Empty;
}
