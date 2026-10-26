using System.Text.Json.Serialization;
using Ambrosia.DTO.Enums;

namespace Ambrosia.DTO.Users;

/// <summary>
/// Perfil do usuário autenticado (FR-003). <b>Não</b> existe nenhum propriedade de senha nesta
/// classe — <c>User.PasswordHash</c> nunca é mapeada para fora da Infra.
/// </summary>
public class UserInfo
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    /// <summary>E-mail normalizado (minúsculas, sem espaços nas pontas).</summary>
    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Provedor de origem do login: <c>google</c> ou <c>dev</c> no JSON (research §7).</summary>
    [JsonPropertyName("provider")]
    public AuthProvider Provider { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }
}
