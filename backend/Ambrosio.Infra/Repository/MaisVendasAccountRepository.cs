using Ambrosio.Domain.Exceptions;
using Ambrosio.Domain.Models;
using Ambrosio.DTO.Enums;
using Ambrosio.Infra.Context;
using Ambrosio.Infra.Interfaces.Repository;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ambrosio.Infra.Repository;

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

    private readonly AmbrosioContext _context;

    public MaisVendasAccountRepository(AmbrosioContext context)
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

    /// <inheritdoc />
    public async Task<DateTime?> GetLastImportedBySellerSinceAsync(long userId, DateTime since)
    {
        // Primeira cláusula sempre o dono (constituição §VI). Só gravação: simulação não atualiza base
        // nenhuma (FR-011). Só succeeded/partial: uma execução que falhou no meio não autoriza o
        // "Atualizar base" (data-model §4 — o estado é um atributo, não um contador).
        //
        // Devolve o instante em vez da entidade porque a camada de interfaces não conhece os modelos do
        // Domain, e nada além da data é perguntado aqui.
        return await _context.ImportRuns
            .AsNoTracking()
            .Where(r => r.UserId == userId)
            .Where(r => r.Mode == ImportRunMode.Apply)
            .Where(r => r.Status == ImportRunStatus.Succeeded || r.Status == ImportRunStatus.Partial)
            .Where(r => r.FinishedAt != null && r.FinishedAt > since)
            .OrderByDescending(r => r.FinishedAt)
            .Select(r => r.FinishedAt)
            .FirstOrDefaultAsync();
    }

    private static bool IsUniqueViolation(Exception ex) =>
        ex is DbUpdateException dbUpdate
        && dbUpdate.InnerException is PostgresException postgres
        && postgres.SqlState == UniqueViolation
        && string.Equals(postgres.ConstraintName, "ux_mais_vendas_accounts_user", StringComparison.Ordinal);
}
