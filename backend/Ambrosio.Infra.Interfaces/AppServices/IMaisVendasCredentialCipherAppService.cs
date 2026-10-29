namespace Ambrosio.Infra.Interfaces.AppServices;

/// <summary>
/// Cifra autenticada das credenciais do +Vendas (feature 008, research R2; constituição §VI, emenda
/// 3.4.1). Guarda login e senha de um serviço de terceiro de forma recuperável — o +Vendas só aceita a
/// senha em claro no login, então hash não serve — sem nunca deixar a chave sair do ambiente.
/// <para>
/// Os <b>dados associados</b> amarram o texto cifrado ao dono e ao campo: copiar a coluna de uma
/// vendedora para outra (ou o login para a coluna da senha) faz a decifração falhar em vez de devolver
/// o segredo de outra pessoa.
/// </para>
/// </summary>
public interface IMaisVendasCredentialCipherAppService
{
    /// <summary>Campo cifrado para a coluna <c>login_encrypted</c>.</summary>
    public const string FIELD_LOGIN = "login";

    /// <summary>Campo cifrado para a coluna <c>password_encrypted</c>.</summary>
    public const string FIELD_PASSWORD = "password";

    /// <summary>
    /// Cifra <paramref name="plain"/> para <paramref name="userId"/>/<paramref name="field"/>. O
    /// resultado tem o prefixo de versão (<c>v1:</c>) e é o que vai para a coluna.
    /// </summary>
    string Encrypt(long userId, string field, string plain);

    /// <summary>
    /// Inverte <see cref="Encrypt"/>.
    /// </summary>
    /// <exception cref="System.Security.Cryptography.CryptographicException">
    /// Payload adulterado, de outro usuário, de outro campo ou com prefixo de versão desconhecido.
    /// </exception>
    string Decrypt(long userId, string field, string payload);
}
