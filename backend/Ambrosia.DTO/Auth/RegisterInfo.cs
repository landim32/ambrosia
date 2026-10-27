using System.Text.Json.Serialization;

namespace Ambrosia.DTO.Auth;

/// <summary>
/// Entrada de <c>POST /auth/register</c> (FR-001, data-model §3): cria a conta com e-mail e
/// senha e já devolve o <see cref="LoginResult"/>. Nenhum <c>userId</c> entra aqui — a API
/// resolve o usuário pelo e-mail normalizado (constituição §VI).
/// </summary>
public class RegisterInfo
{
    /// <summary>Nome de exibição; é <c>Trim()</c> antes de gravar (limite 120).</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>E-mail, normalizado no serviço (<c>Trim().ToLowerInvariant()</c>) e único.</summary>
    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Senha <b>exatamente como digitada</b> (sem <c>Trim()</c> — espaços nas pontas fazem parte
    /// da senha, edge case da spec). Vai só para o hash; nunca é devolvida nem logada (FR-005).
    /// </summary>
    [JsonPropertyName("password")]
    public string Password { get; set; } = string.Empty;
}
