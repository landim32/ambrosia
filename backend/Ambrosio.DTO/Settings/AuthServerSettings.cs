namespace Ambrosio.DTO.Settings;

/// <summary>
/// Configuração do authorization server OAuth 2.1 do plugin do ChatGPT (feature 007, R3/R13/R14).
/// Vive no DTO porque é consumida como <c>IOptions&lt;T&gt;</c> pelas camadas, igual às demais
/// subclasses de <see cref="Ambrosio.Application.Settings.AmbrosioSettings"/>.
/// </summary>
/// <remarks>
/// Não tem <c>[JsonPropertyName]</c>: é tipo de configuração (ligado por <c>GetSection</c> e
/// variáveis de ambiente com <c>__</c>), não tipo de linha. O <c>Issuer</c> aqui é a URL pública do
/// AS (<c>https://ambrosiaai.site/auth</c>) e <b>não</b> é o mesmo conceito do
/// <see cref="AuthSettings.Issuer"/> do JWT HS256 das telas, que é um nome curto — os dois leem a
/// seção <c>Auth</c> em processos diferentes (research R8).
/// </remarks>
public class AuthServerSettings
{
    /// <summary>
    /// Issuer publicado na metadata e no claim <c>iss</c>. Obrigatório, sem barra no final, e
    /// <c>https</c> fora de Development (o startup recusa).
    /// </summary>
    public string Issuer { get; set; } = string.Empty;

    /// <summary>
    /// Chave RSA 2048 em PEM PKCS#8 que assina os tokens. Segredo: obrigatória em Homolog e
    /// Production via variável de ambiente (constituição §VI, emenda 3.4.0). Em Development, vazia
    /// faz o host gerar e reusar uma chave local (R13). Aceita a chave em linha única com
    /// <c>\n</c> literais, como vem do <c>.env</c>.
    /// </summary>
    public string SigningKeyPem { get; set; } = string.Empty;

    /// <summary>
    /// Segredo HMAC do JWT HS256 das telas. O host do MCP não emite esse token, mas
    /// <c>IAuthService.LoginWithPasswordAsync</c> devolve um <c>LoginResult</c> que o inclui, e a
    /// tela de login do AS reaproveita exatamente esse serviço (R10) — então a chave precisa existir
    /// e ter no mínimo 32 bytes ou o login do AS quebra em plena execução.
    /// </summary>
    public string JwtSecret { get; set; } = string.Empty;

    /// <summary>Validade do access token, em minutos (R14; 30 por decisão da spec).</summary>
    public int AccessTokenMinutes { get; set; } = 30;

    /// <summary>Validade do refresh token, em dias, deslizante com rotação (R14).</summary>
    public int RefreshTokenDays { get; set; } = 14;

    /// <summary>Client ID do Google para o botão "Entrar com Google" da tela de login (R10).</summary>
    public string GoogleClientId { get; set; } = string.Empty;

    /// <summary>
    /// Redirect URIs aceitas no registro dinâmico (DCR, RFC 7591). Uma entrada pode terminar em
    /// <c>/{callback_id}</c>, que casa por prefixo com qualquer valor de <c>callback_id</c> — o
    /// padrão que o ChatGPT usa na redirect URI estável.
    /// </summary>
    public List<string> AllowedRedirectUris { get; set; } = [];

    /// <summary>Registro do cliente pelo documento de metadados (CIMD, R4).</summary>
    public CimdSettings Cimd { get; set; } = new();
}

/// <summary>
/// Limites do CIMD (R4): <c>client_id</c> é uma URL https e o AS baixa o documento dela, então o
/// host precisa de lista de hosts permitidos e teto de resposta — sem isso, qualquer URL pública
/// viria um vetor de SSRF.
/// </summary>
public class CimdSettings
{
    /// <summary>Hosts cujo <c>client_id</c> de URL é aceito (padrão: <c>chatgpt.com</c>).</summary>
    public List<string> AllowedHosts { get; set; } = [];
}
