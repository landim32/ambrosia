using Ambrosia.DTO.Enums;
using Ambrosia.DTO.Imports;
using Ambrosia.DTO.Imports.Collector;

namespace Ambrosia.Infra.Interfaces.Repository;

/// <summary>
/// Persistência das execuções de importação (feature 006): <c>import_runs</c>,
/// <c>import_run_blocks</c>, <c>import_run_changes</c> e os upserts de pedido que elas dirigem.
/// <para>
/// <b>Isolamento estrutural (constituição §VI, FR-053)</b>: todo método de <b>leitura</b> recebe
/// <c>userId</c> e a implementação aplica o filtro por usuário como <b>primeira</b> cláusula, de forma
/// que execução de outra vendedora é indistinguível de execução inexistente (o mesmo 404).
/// </para>
/// <para>
/// A única escrita que varre múltiplos usuários é <see cref="MarkInterruptedAsync"/> — e ela é
/// <b>somente escrita em lote</b> (<c>ExecuteUpdateAsync</c>), sem materializar entidade nenhuma e sem
/// devolver dado de usuário, portanto não é método de leitura e fica fora da regra de filtro. É chamada
/// só pelo <c>ImportRunWorker.StartAsync</c>, nunca por um endpoint (T111 confere isso).
/// </para>
/// <para>
/// <c>userId</c> não usa <c>long</c> do DTO: os parâmetros são o mínimo necessário, e nenhuma assinatura
/// aceita credencial — a senha não tem caminho para chegar aqui (FR-006).
/// </para>
/// </summary>
/// <typeparam name="TRun">Modelo da execução.</typeparam>
/// <typeparam name="TBlock">Modelo do bloco.</typeparam>
/// <typeparam name="TChange">Modelo da linha de rastro.</typeparam>
public interface IImportRunRepository<TRun, TBlock, TChange>
    where TRun : class
    where TBlock : class
    where TChange : class
{
    // ---------------------------------------------------------------- reads

    /// <summary>Execução pelo id com seus blocos, só se for da vendedora; <c>null</c> nos dois casos.</summary>
    Task<TRun?> GetByIdAsync(long runId, long userId);

    /// <summary>A execução ativa (<c>queued</c>/<c>running</c>) da vendedora com blocos, ou <c>null</c> (204).</summary>
    Task<TRun?> GetCurrentAsync(long userId);

    /// <summary>Histórico paginado por <c>requestedAt</c> decrescente (FR-045).</summary>
    Task<(List<TRun> Items, int TotalCount)> ListAsync(long userId, int page, int pageSize);

    /// <summary>O rastro pedido a pedido de uma execução da vendedora (FR-048).</summary>
    Task<List<TChange>> ListChangesAsync(long runId, long userId);

    /// <summary>Agrupamento canal × origem × tipo × ação do relatório (FR-049).</summary>
    Task<List<ImportRunBreakdownInfo>> GetBreakdownAsync(long runId, long userId);

    /// <summary>
    /// Quantos pedidos a última execução <c>succeeded</c>/<c>partial</c> em modo gravação do <b>mesmo
    /// período</b> viu (inserir + atualizar + inalterar), excluindo a própria
    /// <paramref name="runId"/> — base do aviso de queda (FR-051, research §18).
    /// </summary>
    Task<int?> GetPreviousRunOrdersCountAsync(long userId, long runId, DateOnly startDate, DateOnly endDate);

    /// <summary>
    /// Data do último pedido importado (<c>MAX(order_date)</c> com <c>source = maisVendas</c>) para a
    /// data inicial sugerida; <c>null</c> quando a vendedora ainda não importou nada (FR-002).
    /// </summary>
    Task<DateOnly?> GetSuggestedStartDateAsync(long userId);

    /// <summary>
    /// Chaves naturais dos pedidos <c>source = maisVendas</c> da vendedora dentro do intervalo do bloco
    /// — é o que produz "não encontrado nesta execução" quando sumiu da origem (FR-027, research §17).
    /// </summary>
    Task<List<ImportedOrderKey>> ListImportedKeysInRangeAsync(long userId, DateOnly from, DateOnly to);

    /// <summary>
    /// Mapa <c>imageUrl → imageId</c> das imagens já ligadas aos itens de um pedido existente
    /// (FR-040): é o que permite pular o download de uma foto que já está no storage para a mesma URL.
    /// </summary>
    Task<Dictionary<string, long>> GetLinkedImageIdsAsync(
        long userId,
        string orderNumber,
        DateOnly orderDate,
        OrderKind kind);

    // ---------------------------------------------------------------- writes

    Task<TRun> InsertAsync(TRun run);

    Task<TRun> UpdateAsync(TRun run);

    Task<TBlock> UpdateBlockAsync(TBlock block);

    Task<TChange> AddChangeAsync(TChange change);

    /// <summary>Marca <c>cancel_requested</c> na execução ativa da vendedora. Retorna quantas linhas mudaram.</summary>
    Task<int> RequestCancelAsync(long runId, long userId);

    /// <summary>
    /// Recupera execuções órfãs depois de reinicialização do servidor (FR-020): toda
    /// <c>queued</c>/<c>running</c> vira <c>failed</c> com a mensagem padrão e seus blocos
    /// <c>pending</c>/<c>running</c> viram <c>notExecuted</c>.
    /// <para>
    /// <b>Somente escrita em lote</b> via <c>ExecuteUpdateAsync</c>: nenhum <c>ToListAsync</c>, nenhum
    /// <c>FirstAsync</c>, nenhuma entidade materializada, e o retorno é só o número de linhas afetadas.
    /// Assim a recuperação no startup não vaza dado nenhum de outro usuário (constituição §VI).
    /// </para>
    /// </summary>
    /// <returns>Total de linhas alteradas (execuções + blocos), para o log.</returns>
    Task<int> MarkInterruptedAsync(DateTime now);

    /// <summary>
    /// Grava <b>um</b> pedido importado na sua própria transação (research §7): cliente sob demanda por
    /// <c>cpf_hash</c>, upsert pela chave natural <c>(user_id, order_number, order_date, kind)</c> e
    /// substituição das linhas. Falha ⇒ rollback só daquele pedido, e o bloco continua (FR-012).
    /// </summary>
    /// <param name="userId">Dono, do job em execução.</param>
    /// <param name="snapshot">Pedido normalizado vindo do coletor.</param>
    /// <param name="cpfHash">HMAC do CPF, para achar/criar o cliente.</param>
    /// <param name="cpfEncrypted">Cifra autenticada do CPF (só na criação do cliente).</param>
    /// <param name="cpfMasked">Máscara para exibição (só na criação do cliente).</param>
    /// <param name="sourceHash">Impressão digital do conteúdo (research §6).</param>
    /// <param name="imageIdsByUrl">Fotos já disponíveis nesta execução, para ligar às linhas (FR-037).</param>
    /// <param name="now">Instante UTC da captura.</param>
    Task<UpsertOutcome> UpsertImportedOrderAsync(
        long userId,
        CollectedOrder snapshot,
        string cpfHash,
        string cpfEncrypted,
        string cpfMasked,
        string sourceHash,
        IReadOnlyDictionary<string, long> imageIdsByUrl,
        DateTime now);

    /// <summary>
    /// Mesma decisão do upsert, <b>sem gravar nada</b> (FR-032, simulação): <c>AsNoTracking</c> em tudo
    /// e nenhuma transação aberta. Devolve a ação que o upsert tomaria, os campos que mudariam e se o
    /// cliente seria criado.
    /// </summary>
    Task<UpsertOutcome> PreviewImportedOrderAsync(
        long userId,
        CollectedOrder snapshot,
        string cpfHash,
        string sourceHash);
}
