namespace Ambrosio.Infra.Interfaces.Repository;

/// <summary>
/// Persistência do vínculo da vendedora com a conta do +Vendas, do bloqueio por recusas
/// (feature 006, tabela <c>mais_vendas_accounts</c>, research §8) e das credenciais cifradas
/// (feature 008).
/// <para>
/// Uma linha por vendedora (<c>ux_mais_vendas_accounts_user</c>). Os métodos que escrevem credencial
/// recebem o texto JÁ cifrado: a senha em claro não tem caminho até o banco (FR-003 da 008), e a
/// cifra é função da Infra, não daqui.
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

    /// <summary>
    /// Quando terminou a última execução <c>succeeded</c>/<c>partial</c> em modo gravação da
    /// vendedora terminada depois de <paramref name="since"/>, ou <c>null</c> quando não houve uma
    /// dentro do corte (feature 008, FR-011/FR-015: é o que autoriza o "Atualizar base").
    /// <para>
    /// Devolve o instante e não a execução: <c>Ambrosio.Infra.Interfaces</c> é de dependência zero e
    /// não conhece os modelos do Domain (skill <c>dotnet-architecture</c>), e nada além desta data é
    /// necessário para decidir disponibilidade — o período vem da data do último pedido importado, que
    /// <c>IImportRunRepository.GetSuggestedStartDateAsync</c> já responde.
    /// </para>
    /// </summary>
    Task<DateTime?> GetLastImportedBySellerSinceAsync(long userId, DateTime since);
}
