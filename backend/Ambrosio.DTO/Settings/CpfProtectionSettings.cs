namespace Ambrosio.DTO.Settings;

/// <summary>
/// Segredos de proteção do CPF (research §4): <see cref="Pepper"/> alimenta o HMAC-SHA256
/// do <c>cpf_hash</c>; <see cref="EncryptionKey"/> alimenta o AES-256-GCM do <c>cpf_encrypted</c>.
/// </summary>
public class CpfProtectionSettings
{
    /// <summary>Chave do HMAC-SHA256 (UTF-8). Obrigatória, nunca vazia (research §16).</summary>
    public string Pepper { get; set; } = string.Empty;

    /// <summary>Chave AES-256 em base64 de exatamente 32 bytes. Obrigatória.</summary>
    public string EncryptionKey { get; set; } = string.Empty;
}
