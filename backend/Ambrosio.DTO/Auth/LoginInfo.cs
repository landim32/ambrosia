using System.Text.Json.Serialization;

namespace Ambrosio.DTO.Auth;

/// <summary>
/// Entrada de <c>POST /auth/login</c> (FR-002, data-model §3). O comprimento da senha não é
/// validado aqui de propósito: qualquer senha errada cai no 401 genérico (FR-008), e validar
/// 8–128 no login revelaria informação sobre contas antigas.
/// </summary>
public class LoginInfo
{
    /// <summary>E-mail, normalizado no serviço para a busca.</summary>
    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    /// <summary>Senha como digitada; só usada na verificação do hash (nunca devolvida).</summary>
    [JsonPropertyName("password")]
    public string Password { get; set; } = string.Empty;
}
