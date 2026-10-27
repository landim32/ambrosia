namespace Ambrosio.Infra.Interfaces.AppServices;

/// <summary>
/// Hash e verificação de senha (research §1 e §2, FR-001/FR-005). Preocupação criptográfica
/// da Infra — mesmo padrão de <see cref="ICpfProtectionAppService"/> — para que o Domain não
/// dependa de <c>System.Security.Cryptography</c> e o algoritmo possa ser trocado sem tocar
/// nas regras de negócio.
/// </summary>
public interface IPasswordHashAppService
{
    /// <summary>
    /// Deriva o hash autodescritivo da senha (algoritmo, iterações, salt e digest), pronto para
    /// <c>users.password_hash</c>. O salt é aleatório: duas senhas iguais produzem hashes
    /// diferentes (FR-005).
    /// </summary>
    /// <param name="password">Senha exatamente como recebida — sem <c>Trim()</c>.</param>
    string Hash(string password);

    /// <summary>
    /// Confere a senha contra o hash armazenado, em tempo constante, lendo as iterações do
    /// próprio hash (permite rehash futuro sem migração).
    /// </summary>
    /// <param name="password">Senha digitada.</param>
    /// <param name="passwordHash">Hash armazenado.</param>
    /// <returns>
    /// <c>false</c> quando o hash é nulo, malformado ou de algoritmo desconhecido — nunca lança.
    /// </returns>
    bool Verify(string password, string? passwordHash);

    /// <summary>
    /// Hash de uma senha aleatória, usado para manter o custo do login inválido igual ao do
    /// login válido quando o usuário não existe ou não tem senha (research §2, SC-006).
    /// </summary>
    string DummyHash { get; }
}
