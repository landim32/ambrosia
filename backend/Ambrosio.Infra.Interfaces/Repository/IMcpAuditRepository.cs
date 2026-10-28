namespace Ambrosio.Infra.Interfaces.Repository;

/// <summary>
/// Trilha das gravações feitas pelo plugin do ChatGPT (tabela <c>mcp_audit_entries</c>, feature 007,
/// R16). Só insere e lê: uma entrada de auditoria nunca é alterada nem apagada pelo sistema.
/// </summary>
public interface IMcpAuditRepository<TModel> where TModel : class
{
    Task<TModel> InsertAsync(TModel entity);

    /// <summary>
    /// Últimas entradas do próprio usuário, mais recentes primeiro. <paramref name="userId"/> é a
    /// primeira cláusula, como em toda leitura (constituição §VI).
    /// </summary>
    Task<IReadOnlyList<TModel>> ListRecentAsync(long userId, string? toolName, int limit);
}
