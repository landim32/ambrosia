using Ambrosio.Domain.Exceptions;
using Ambrosio.Domain.Models;
using Ambrosio.Infra.Context;
using Ambrosio.Infra.Interfaces.Repository;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ambrosio.Infra.Repository;

/// <summary>
/// Acesso à tabela <c>users</c>. A leitura usa <c>AsNoTracking</c> e a atualização reescreve os
/// valores escalares da entidade carregada (padrão da skill <c>dotnet-architecture</c>), porque
/// cada login pode trazer nome/subject novos do provedor.
/// </summary>
public class UserRepository : IUserRepository<User>
{
    /// <summary>SQLSTATE de violação de unicidade no PostgreSQL (research §11).</summary>
    private const string UniqueViolation = "23505";

    private readonly AmbrosioContext _context;

    public UserRepository(AmbrosioContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task<User?> GetByIdAsync(long userId)
    {
        return await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId);
    }

    /// <inheritdoc />
    public async Task<User?> GetByEmailAsync(string normalizedEmail)
    {
        return await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email == normalizedEmail);
    }

    /// <inheritdoc />
    public async Task<User> InsertAsync(User entity)
    {
        _context.Users.Add(entity);

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Duas sessões do mesmo e-mail no primeiro login: o índice único ux_users_email
            // decide a corrida e o serviço relê o usuário (research §11).
            throw new DuplicateException("Já existe um usuário com este e-mail.");
        }

        return entity;
    }

    /// <inheritdoc />
    public async Task<User> UpdateAsync(User entity)
    {
        var existing = await _context.Users.FirstOrDefaultAsync(u => u.Id == entity.Id);

        if (existing is null)
        {
            throw new NotFoundException("Usuário não encontrado.");
        }

        _context.Entry(existing).CurrentValues.SetValues(entity);
        await _context.SaveChangesAsync();

        return existing;
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: UniqueViolation };
}
