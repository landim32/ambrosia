using Ambrosio.DTO.Auth;

namespace Ambrosio.Domain.Interfaces;

/// <summary>
/// Fluxos de cadastro e login da API (FR-001, FR-002, FR-009). Todos terminam com o mesmo
/// JWT do Ambrosio e o perfil do usuário criado ou reutilizado. Não existe emissão de token
/// sem credencial: o atalho de desenvolvimento foi removido de todos os ambientes
/// (constituição §VI).
/// </summary>
public interface IAuthService
{
    /// <summary>
    /// Cria a conta com e-mail e senha (hash em <c>users.password_hash</c>) e já devolve a
    /// sessão — o cadastro não exige um segundo passo de login (FR-001, research §3).
    /// </summary>
    /// <exception cref="Exceptions.DuplicateException">E-mail já cadastrado (HTTP 409).</exception>
    Task<LoginResult> RegisterAsync(RegisterInfo info);

    /// <summary>
    /// Entra com e-mail e senha, devolvendo o mesmo <see cref="LoginResult"/> do Google
    /// (FR-002/FR-007).
    /// </summary>
    /// <exception cref="Exceptions.InvalidCredentialsException">
    /// E-mail inexistente, senha incorreta ou conta sem senha — sempre a mesma mensagem (FR-008).
    /// </exception>
    Task<LoginResult> LoginWithPasswordAsync(LoginInfo info);

    /// <summary>
    /// Troca um ID token do Google por um JWT do Ambrosio. Reutilizando uma conta que tem
    /// senha, a senha é removida (FR-009).
    /// </summary>
    Task<LoginResult> LoginWithGoogleAsync(GoogleLoginInfo info);
}
