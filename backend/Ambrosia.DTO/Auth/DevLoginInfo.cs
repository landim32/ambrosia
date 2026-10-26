using System.Text.Json.Serialization;

namespace Ambrosia.DTO.Auth;

/// <summary>
/// Entrada de <c>POST /auth/dev-login</c> (research §2). Disponível apenas em Development;
/// emite o JWT do Ambrosia sem passar pelo Google. Não há senha — por isso a rota é bloqueada
/// por ambiente e o token nunca sai da API em produção.
/// </summary>
public class DevLoginInfo
{
    /// <summary>E-mail do usuário (normalizado no serviço: <c>Trim().ToLowerInvariant()</c>).</summary>
    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    /// <summary>Nome exibido no perfil.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}
