using System.Text.Json.Serialization;
using Ambrosio.DTO.Users;

namespace Ambrosio.DTO.Auth;

/// <summary>
/// Resposta dos dois fluxos de login (FR-002): o JWT do Ambrosio, sua expiração e o perfil do
/// usuário recém-criado ou reutilizado.
/// </summary>
public class LoginResult
{
    /// <summary>JWT HS256 do Ambrosio (claim <c>sub</c> = <c>user_id</c>).</summary>
    [JsonPropertyName("token")]
    public string Token { get; set; } = string.Empty;

    /// <summary>Momento de expiração do token (UTC), igual ao claim <c>exp</c>.</summary>
    [JsonPropertyName("expiresAt")]
    public DateTime ExpiresAt { get; set; }

    /// <summary>Perfil do usuário, sem qualquer campo de senha (FR-003).</summary>
    [JsonPropertyName("user")]
    public UserInfo User { get; set; } = new();
}
