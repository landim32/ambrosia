using Ambrosia.DTO.Users;

namespace Ambrosia.Domain.Interfaces;

/// <summary>Leitura do perfil do próprio usuário (FR-003).</summary>
public interface IUserService
{
    /// <summary>
    /// Perfil do usuário autenticado, sem qualquer campo de senha.
    /// </summary>
    /// <exception cref="Exceptions.NotFoundException">Usuário inexistente (token antigo).</exception>
    Task<UserInfo> GetByIdAsync(long userId);
}
