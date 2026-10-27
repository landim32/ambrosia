namespace Ambrosia.Infra.Interfaces.Repository;

/// <summary>
/// Persistência do vínculo da vendedora com a conta do +Vendas e do bloqueio por recusas
/// (feature 006, tabela <c>mais_vendas_accounts</c>, research §8).
/// <para>
/// Uma linha por vendedora (<c>ux_mais_vendas_accounts_user</c>). Os dois métodos que escrevem nunca
/// recebem login, senha ou o identificador da conta — só o hash já calculado pelo AppService, para que
/// a credencial não tenha caminho até o banco (FR-006).
/// </para>
/// </summary>
/// <typeparam name="TModel">Modelo do vínculo.</typeparam>
public interface IMaisVendasAccountRepository<TModel> where TModel : class
{
    /// <summary>
    /// Linha da vendedora, criando-a se ainda não existir. A corrida entre duas requisições da mesma
    /// vendedora é resolvida pelo índice único: a perdida relê e devolve a linha vencedora (research §8).
    /// </summary>
    Task<TModel> GetOrCreateAsync(long userId, DateTime now);

    Task<TModel> UpdateAsync(TModel entity);
}
