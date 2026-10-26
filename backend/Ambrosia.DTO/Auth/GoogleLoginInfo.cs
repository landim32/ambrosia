using System.Text.Json.Serialization;

namespace Ambrosia.DTO.Auth;

/// <summary>
/// Entrada de <c>POST /auth/google</c> (FR-001). O cliente obtém o ID token do Google por conta
/// própria e o troca por um JWT do Ambrosia (research §1).
/// </summary>
public class GoogleLoginInfo
{
    /// <summary>ID token (JWT) emitido pelo Google, com <c>aud</c> = <c>Auth:GoogleClientId</c>.</summary>
    [JsonPropertyName("idToken")]
    public string IdToken { get; set; } = string.Empty;
}
