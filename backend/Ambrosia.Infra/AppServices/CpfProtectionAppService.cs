using System.Security.Cryptography;
using System.Text;
using Ambrosia.DTO.Settings;
using Ambrosia.Infra.Interfaces.AppServices;
using Microsoft.Extensions.Options;

namespace Ambrosia.Infra.AppServices;

/// <summary>
/// Proteção do CPF (research §4, FR-011).
/// <para>
/// <b>Hash</b>: HMAC-SHA256 dos 11 dígitos com o pepper em UTF-8, em hex minúsculo (64 chars). O
/// pepper impede ataque de dicionário sobre os 10^11 documentos possíveis — SHA-256 puro seria
/// quebrável. Serve à unicidade <c>(user_id, cpf_hash)</c>.
/// </para>
/// <para>
/// <b>Cifra</b>: AES-256-GCM com nonce aleatório de 12 bytes e tag de 16, serializada como
/// base64 de <c>nonce || tag || ciphertext</c>. O nonce aleatório faz duas cifras do mesmo CPF
/// diferirem, e a tag garante que adulteração falhe em vez de devolver lixo.
/// </para>
/// </summary>
public class CpfProtectionAppService : ICpfProtectionAppService
{
    /// <summary>Tamanho do nonce exigido pelo AES-GCM.</summary>
    private const int NONCE_SIZE = 12;

    /// <summary>Tamanho da tag de autenticação do AES-GCM.</summary>
    private const int TAG_SIZE = 16;

    private readonly CpfProtectionSettings _settings;

    public CpfProtectionAppService(IOptions<CpfProtectionSettings> settings)
    {
        _settings = settings.Value;
    }

    /// <inheritdoc />
    public string Hash(string cpfDigits)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_settings.Pepper));
        var bytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(cpfDigits));

        // Convert.ToHexString devolve maiúsculas; o contrato é hex minúsculo de 64 chars
        // (coluna cpf_hash varchar(64)).
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <inheritdoc />
    public string Encrypt(string cpfDigits)
    {
        using var aes = new AesGcm(GetKey(), TAG_SIZE);

        var plain = Encoding.UTF8.GetBytes(cpfDigits);
        var nonce = RandomNumberGenerator.GetBytes(NONCE_SIZE);
        var cipher = new byte[plain.Length];
        var tag = new byte[TAG_SIZE];

        aes.Encrypt(nonce, plain, cipher, tag);

        return Convert.ToBase64String(Compose(nonce, tag, cipher));
    }

    /// <inheritdoc />
    public string Decrypt(string payload)
    {
        var raw = Convert.FromBase64String(payload);

        if (raw.Length < NONCE_SIZE + TAG_SIZE + 1)
        {
            throw new CryptographicException("Payload cifrado do CPF é inválido.");
        }

        var nonce = raw[..NONCE_SIZE];
        var tag = raw[NONCE_SIZE..(NONCE_SIZE + TAG_SIZE)];
        var cipher = raw[(NONCE_SIZE + TAG_SIZE)..];

        using var aes = new AesGcm(GetKey(), TAG_SIZE);

        var plain = new byte[cipher.Length];

        // A tag é verificada aqui: qualquer byte alterado (ou chave diferente) lança
        // AuthenticationTagMismatchException, derivada de CryptographicException.
        aes.Decrypt(nonce, cipher, tag, plain);

        return Encoding.UTF8.GetString(plain);
    }

    private byte[] GetKey()
    {
        var key = Convert.FromBase64String(_settings.EncryptionKey);

        // A constituição §VI/skill dotnet-env exigem 32 bytes; Program.cs já valida no startup,
        // e esta checagem protege contra configuração carregada por outro caminho (ex.: testes).
        if (key.Length != 32)
        {
            throw new CryptographicException(
                "CpfProtection:EncryptionKey deve ser base64 de exatamente 32 bytes.");
        }

        return key;
    }

    private static byte[] Compose(byte[] nonce, byte[] tag, byte[] cipher)
    {
        var payload = new byte[nonce.Length + tag.Length + cipher.Length];

        Buffer.BlockCopy(nonce, 0, payload, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, payload, nonce.Length, tag.Length);
        Buffer.BlockCopy(cipher, 0, payload, nonce.Length + tag.Length, cipher.Length);

        return payload;
    }
}
