using Ambrosio.DTO.Auth;

namespace Ambrosio.Infra.Interfaces.Repository;

/// <summary>
/// Persistência de usuários (tabela <c>users</c>). Genérica quanto ao modelo, conforme a skill
/// <c>dotnet-architecture</c>: a interface não conhece <c>Ambrosio.Domain</c>.
/// </summary>
/// <typeparam name="TModel">Modelo do usuário.</typeparam>
public interface IUserRepository<TModel> where TModel : class
{
    /// <summary>Busca pelo identificador (claim <c>sub</c> do JWT).</summary>
    Task<TModel?> GetByIdAsync(long userId);

    /// <summary>
    /// Busca pelo e-mail <b>normalizado</b> (<c>Trim().ToLowerInvariant()</c>, pesquisa exata).
    /// </summary>
    Task<TModel?> GetByEmailAsync(string normalizedEmail);

    /// <summary>
    /// Insere o usuário. Violação do índice único <c>ux_users_email</c> é convertida pela
    /// implementação em <c>DuplicateException</c> (HTTP 409).
    /// </summary>
    Task<TModel> InsertAsync(TModel entity);

    /// <summary>Atualiza nome/provedor no login seguinte.</summary>
    Task<TModel> UpdateAsync(TModel entity);
}
