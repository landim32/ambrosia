namespace Ambrosia.Infra.Interfaces.AppServices;

/// <summary>
/// Emissão do JWT do Ambrosia usado em todas as chamadas autenticadas (research §3,
/// constituição §VI).
/// </summary>
public interface IJwtTokenAppService
{
    /// <summary>
    /// Gera o token HS256 com os claims <c>sub</c>, <c>email</c> e <c>name</c>.
    /// </summary>
    /// <param name="userId">Identificador do usuário — vai no claim <c>sub</c> (FR-021).</param>
    /// <param name="email">E-mail normalizado.</param>
    /// <param name="name">Nome de exibição.</param>
    /// <returns>O token e o instante UTC em que expira (claim <c>exp</c>).</returns>
    (string Token, DateTime ExpiresAt) Generate(long userId, string email, string name);
}
