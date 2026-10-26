namespace Ambrosia.DTO.Settings;

/// <summary>
/// Configuração do esquema de autenticação JWT Bearer (research §3, §16).
/// Lida da seção <c>Auth</c> do appsettings/variáveis de ambiente.
/// </summary>
public class AuthSettings
{
    /// <summary>Segredo simétrico HS256. Obrigatório, mínimo 32 bytes UTF-8 (research §16).</summary>
    public string JwtSecret { get; set; } = string.Empty;

    /// <summary>Emissor validado/emitido (<c>iss</c>).</summary>
    public string Issuer { get; set; } = "ambrosia";

    /// <summary>Destinatário validado/emitido (<c>aud</c>).</summary>
    public string Audience { get; set; } = "ambrosia";

    /// <summary>Duração da sessão em minutos (padrão 480 = 8h, research §3).</summary>
    public int ExpirationMinutes { get; set; } = 480;

    /// <summary>Client ID do Google usado como audiência na validação do ID token.</summary>
    public string GoogleClientId { get; set; } = string.Empty;
}
