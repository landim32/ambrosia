namespace Ambrosia.Infra.Interfaces.AppServices;

/// <summary>
/// Proteção do CPF (research §4, FR-011): hash determinístico para unicidade, cifra autenticada
/// para o detalhe do cliente. A máscara é calculada no Domain (value object <c>Cpf</c>), não aqui.
/// </summary>
public interface ICpfProtectionAppService
{
    /// <summary>HMAC-SHA256 em hex minúsculo (64 chars) dos 11 dígitos, com o pepper configurado.</summary>
    string Hash(string cpfDigits);

    /// <summary>AES-256-GCM → base64 de <c>nonce || tag || ciphertext</c> (varchar(120)).</summary>
    string Encrypt(string cpfDigits);

    /// <summary>Inverte <see cref="Encrypt"/>; lança <c>CryptographicException</c> se o payload foi adulterado.</summary>
    string Decrypt(string payload);
}
