using Ambrosia.Domain.Exceptions;
using Ambrosia.Domain.Interfaces;
using Ambrosia.Domain.Models;
using Ambrosia.DTO.Users;
using Ambrosia.Infra.Interfaces.Repository;

namespace Ambrosia.Domain.Services;

/// <summary>
/// Perfil do usuário autenticado (FR-003). Mapeamento manual, campo a campo: como não há
/// mapeador por convenção/reflexão, <see cref="User.PasswordHash"/> não tem como chegar ao DTO.
/// </summary>
public class UserService : IUserService
{
    private readonly IUserRepository<User> _users;

    public UserService(IUserRepository<User> users)
    {
        _users = users;
    }

    /// <inheritdoc />
    public async Task<UserInfo> GetByIdAsync(long userId)
    {
        var user = await _users.GetByIdAsync(userId);

        if (user is null)
        {
            throw new NotFoundException("Usuário não encontrado.");
        }

        return new UserInfo
        {
            Id = user.Id,
            Email = user.Email,
            Name = user.Name,
            Provider = user.Provider,
            CreatedAt = user.CreatedAt
        };
    }
}
