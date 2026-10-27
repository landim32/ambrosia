using Ambrosia.Domain.Exceptions;
using Ambrosia.Domain.Models;
using Ambrosia.Infra.Context;
using Ambrosia.Infra.Interfaces.Repository;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ambrosia.Infra.Repository;

/// <summary>
/// Persistência do vínculo da vendedora com a conta do +Vendas e do bloqueio por recusas
/// (feature 006, tabela <c>mais_vendas_accounts</c>).
/// <para>
/// Uma linha por vendedora, garantida por <c>ux_mais_vendas_accounts_user</c>. Nenhum método aceita
/// login, senha ou o identificador da conta: quem calcula o hash é o AppService, e aqui só chega o
/// hash — a credencial não tem caminho até o banco (FR-006).
/// </para>
/// </summary>
public class MaisVendasAccountRepository : IMaisVendasAccountRepository<MaisVendasAccount>
{
    /// <summary>SQLSTATE de violação de unicidade.</summary>
    private const string UniqueViolation = "23505";

    private readonly AmbrosiaContext _context;

    public MaisVendasAccountRepository(AmbrosiaContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task<MaisVendasAccount> GetOrCreateAsync(long userId, DateTime now)
    {
        var account = await _context.MaisVendasAccounts
            .FirstOrDefaultAsync(a => a.UserId == userId);

        if (account is not null)
        {
            return account;
        }

        account = MaisVendasAccount.Create(userId, now);
        _context.MaisVendasAccounts.Add(account);

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Corrida: a mesma vendedora abriu duas execuções ao mesmo tempo (ou um POST /runs e uma
            // execução em curso) e as duas tentaram criar a linha. O índice único decidiu; relê-se uma
            // vez a linha vencedora — ela já existe, então a segunda tentativa não precisa de nova INSERT.
            account = await _context.MaisVendasAccounts
                .FirstAsync(a => a.UserId == userId);
        }

        return account;
    }

    /// <inheritdoc />
    public async Task<MaisVendasAccount> UpdateAsync(MaisVendasAccount entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        var existing = await _context.MaisVendasAccounts
            .FirstOrDefaultAsync(a => a.Id == entity.Id && a.UserId == entity.UserId);

        if (existing is null)
        {
            throw new NotFoundException("Vínculo com o +Vendas não encontrado.");
        }

        _context.Entry(existing).CurrentValues.SetValues(entity);
        await _context.SaveChangesAsync();

        return existing;
    }

    private static bool IsUniqueViolation(Exception ex) =>
        ex is DbUpdateException dbUpdate
        && dbUpdate.InnerException is PostgresException postgres
        && postgres.SqlState == UniqueViolation
        && string.Equals(postgres.ConstraintName, "ux_mais_vendas_accounts_user", StringComparison.Ordinal);
}
