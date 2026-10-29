using System.Security.Cryptography;
using System.Text;
using Ambrosio.DTO.Settings;
using Ambrosio.Infra.Interfaces.AppServices;
using Microsoft.Extensions.Options;

namespace Ambrosio.Infra.AppServices;

/// <summary>
/// AES-256-GCM das credenciais do +Vendas (feature 008, research R2; constituição §VI, emenda 3.4.1).
/// <para>
/// Formato gravado: <c>v1:</c> + base64 de <c>nonce ‖ tag ‖ ciphertext</c>, com nonce aleatório de 12
/// bytes por gravação — duas gravações da mesma senha produzem textos diferentes, e a tag faz
/// adulteração falhar em vez de devolver lixo. O prefixo de versão deixa espaço para rotação de chave
/// sem migração de esquema.
/// </para>
/// <para>
/// Os dados associados são <c>mais-vendas:{userId}:{field}</c>: a cifra só volta em claro para o dono e
/// no campo originais. É a trava que impede uma <c>UPDATE</c> maliciosa ou acidental de cruzar a senha
/// de uma vendedora com o login de outra.
/// </para>
/// <para>
/// Mesma estrutura do <see cref="CpfProtectionAppService"/>, com chave à parte: um vazamento da chave do
/// CPF não abre as credenciais (research R2, alternativa descartada).
/// </para>
/// </summary>
public class MaisVendasCredentialCipherAppService : IMaisVendasCredentialCipherAppService
{
    /// <summary>Prefixo de versão do formato, para permitir rotação de chave sem mudar o esquema.</summary>
    private const string VERSION_PREFIX = "v1:";

    /// <summary>Tamanho do nonce exigido pelo AES-GCM.</summary>
    private const int NONCE_SIZE = 12;

    /// <summary>Tamanho da tag de autenticação do AES-GCM.</summary>
    private const int TAG_SIZE = 16;

    private readonly MaisVendasSettings _settings;

    public MaisVendasCredentialCipherAppService(IOptions<MaisVendasSettings> settings)
    {
        _settings = settings.Value;
    }

    /// <inheritdoc />
    public string Encrypt(long userId, string field, string plain)
    {
        ArgumentNullException.ThrowIfNull(field);

        using var aes = new AesGcm(GetKey(), TAG_SIZE);

        var associatedData = AssociatedData(userId, field);
        var bytes = Encoding.UTF8.GetBytes(plain ?? string.Empty);
        var nonce = RandomNumberGenerator.GetBytes(NONCE_SIZE);
        var cipher = new byte[bytes.Length];
        var tag = new byte[TAG_SIZE];

        aes.Encrypt(nonce, bytes, cipher, tag, associatedData);

        return VERSION_PREFIX + Convert.ToBase64String(Compose(nonce, tag, cipher));
    }

    /// <inheritdoc />
    public string Decrypt(long userId, string field, string payload)
    {
        ArgumentNullException.ThrowIfNull(field);

        if (string.IsNullOrEmpty(payload) || !payload.StartsWith(VERSION_PREFIX, StringComparison.Ordinal))
        {
            throw new CryptographicException(
                "Credencial do +Vendas em formato não reconhecido; cadastre de novo.");
        }

        var raw = ReadBase64(payload[VERSION_PREFIX.Length..]);

        if (raw.Length < NONCE_SIZE + TAG_SIZE + 1)
        {
            throw new CryptographicException("Payload cifrado da credencial do +Vendas é inválido.");
        }

        var nonce = raw[..NONCE_SIZE];
        var tag = raw[NONCE_SIZE..(NONCE_SIZE + TAG_SIZE)];
        var cipher = raw[(NONCE_SIZE + TAG_SIZE)..];

        using var aes = new AesGcm(GetKey(), TAG_SIZE);

        var bytes = new byte[cipher.Length];

        // A tag e os dados associados são verificados aqui: payload de outro usuário, de outro campo,
        // adulterado ou cifrado com outra chave lança AuthenticationTagMismatchException — derivada de
        // CryptographicException — e nunca devolve o texto claro.
        aes.Decrypt(nonce, cipher, tag, bytes, AssociatedData(userId, field));

        return Encoding.UTF8.GetString(bytes);
    }

    private byte[] GetKey()
    {
        var key = ReadBase64(_settings.CredentialsKey);

        // A constituição §VI (emenda 3.4.1) exige 32 bytes; Program.cs já recusa o startup fora disso,
        // e esta checagem protege contra configuração carregada por outro caminho (ex.: testes).
        if (key.Length != 32)
        {
            throw new CryptographicException(
                "MaisVendas:CredentialsKey deve ser base64 de exatamente 32 bytes.");
        }

        return key;
    }

    /// <summary>
    /// Amarra a cifra ao dono e ao campo (<c>mais-vendas:{userId}:{field}</c>, research R2). Não contém
    /// segredo algum: é texto conhecido dos dois lados, e o que se protege é a troca de contexto.
    /// </summary>
    private static byte[] AssociatedData(long userId, string field) =>
        Encoding.UTF8.GetBytes($"mais-vendas:{userId}:{field}");

    private static byte[] ReadBase64(string value)
    {
        try
        {
            return Convert.FromBase64String(value);
        }
        catch (FormatException ex)
        {
            throw new CryptographicException("Payload cifrado da credencial do +Vendas é inválido.", ex);
        }
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
