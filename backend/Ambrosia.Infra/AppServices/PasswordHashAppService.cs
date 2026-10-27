using System.Security.Cryptography;
using System.Text;
using Ambrosia.Infra.Interfaces.AppServices;

namespace Ambrosia.Infra.AppServices;

/// <summary>
/// Hash de senha em PBKDF2-HMAC-SHA256 (research §1), com a BCL do .NET 8 — nenhum pacote
/// novo (constituição §II). O custo alto é deliberado: ~150–300 ms por verificação, dentro da
/// meta de 500 ms p95, e é o que torna inviável um ataque de dicionário sobre a tabela.
/// <para>
/// Formato persistido em <c>users.password_hash</c> (~90 caracteres, cabe no
/// <c>varchar(260)</c> existente):
/// </para>
/// <code>pbkdf2-sha256$600000$&lt;salt base64&gt;$&lt;hash base64&gt;</code>
/// <para>
/// O algoritmo e o número de iterações vão no próprio hash, então uma futura subida de custo
/// não exige migração: <see cref="Verify"/> lê as iterações do valor armazenado.
/// </para>
/// </summary>
public class PasswordHashAppService : IPasswordHashAppService
{
    /// <summary>Iterações recomendadas pela OWASP para PBKDF2-SHA256.</summary>
    private const int PBKDF2_ITERATIONS = 600000;

    /// <summary>Tamanho do salt aleatório por senha (FR-005).</summary>
    private const int SALT_SIZE = 16;

    /// <summary>Tamanho do digest derivado.</summary>
    private const int HASH_SIZE = 32;

    /// <summary>Prefixo do formato; <see cref="Verify"/> recusa qualquer outro algoritmo.</summary>
    private const string ALGORITHM = "pbkdf2-sha256";

    private const char SEPARATOR = '$';

    private const int FORMAT_PARTS = 4;

    /// <summary>
    /// As iterações são lidas do hash, mas um valor absurdamente alto seria um vetor de DoS por
    /// corpo de requisição; o limite superior casa com a meta de 500 ms p95.
    /// </summary>
    private const int MAX_ITERATIONS = 2_000_000;

    /// <summary>
    /// Hash de uma senha aleatória gerada no construtor: verificar contra ele custa exatamente o
    /// mesmo que verificar a senha de um usuário real, e é isso que iguala o tempo de resposta
    /// entre "e-mail inexistente" e "senha errada" (research §2, SC-006).
    /// </summary>
    public string DummyHash { get; }

    public PasswordHashAppService()
    {
        DummyHash = Hash(Convert.ToHexString(RandomNumberGenerator.GetBytes(HASH_SIZE)));
    }

    /// <inheritdoc />
    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SALT_SIZE);
        var digest = Derive(password, salt, PBKDF2_ITERATIONS);

        return string.Join(SEPARATOR, ALGORITHM, PBKDF2_ITERATIONS, ToBase64(salt), ToBase64(digest));
    }

    /// <inheritdoc />
    public bool Verify(string password, string? passwordHash)
    {
        if (string.IsNullOrEmpty(passwordHash))
        {
            return false;
        }

        var parts = passwordHash.Split(SEPARATOR);

        if (parts.Length != FORMAT_PARTS || parts[0] != ALGORITHM)
        {
            return false;
        }

        if (!int.TryParse(parts[1], out var iterations)
            || iterations < 1
            || iterations > MAX_ITERATIONS)
        {
            return false;
        }

        byte[] salt;
        byte[] expected;

        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        if (salt.Length == 0 || expected.Length == 0)
        {
            return false;
        }

        var actual = Derive(password, salt, iterations);

        // FixedTimeEquals: sem ela, a comparação pararia no primeiro byte diferente e o tempo
        // de resposta vazaria informação sobre o hash.
        return actual.Length == expected.Length
            && CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static byte[] Derive(string password, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            HASH_SIZE);

    private static string ToBase64(byte[] bytes) => Convert.ToBase64String(bytes);
}
