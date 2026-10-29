using Ambrosio.Domain.Common;
using Ambrosio.Domain.Exceptions;
using Ambrosio.Domain.Models;
using Ambrosio.DTO.Enums;
using Ambrosio.DTO.Imports;
using Ambrosio.DTO.Imports.Collector;
using Ambrosio.Infra.Context;
using Ambrosio.Infra.Interfaces.Repository;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ambrosio.Infra.Repository;

/// <summary>
/// Persistência da importação do +Vendas (feature 006): <c>import_runs</c>, <c>import_run_blocks</c>,
/// <c>import_run_changes</c> e o upsert de pedido que eles dirigem.
/// <para>
/// <b>Isolamento (constituição §VI)</b>: toda leitura começa por <c>Where(r => r.UserId == userId)</c>,
/// e "não existe" e "é de outra vendedora" respondem idêntico. A única exceção é
/// <see cref="MarkInterruptedAsync"/>, que é <b>somente escrita em lote</b> — dois
/// <c>ExecuteUpdateAsync</c> que não materializam entidade nenhuma e não devolvem dado de usuário —
/// chamada apenas pelo <c>ImportRunWorker.StartAsync</c>, nunca por um endpoint.
/// </para>
/// <para>
/// <see cref="UpsertImportedOrderAsync"/> é o ponto de gravação de <b>um</b> pedido (research §7): abre
/// a própria transação, porque a atomicidade exigida é por pedido — falha no 15º não desfaz os 14
/// anteriores (FR-012), e não existe transação de bloco a manter aberta durante minutos de coleta.
/// </para>
/// </summary>
public class ImportRunRepository : IImportRunRepository<ImportRun, ImportRunBlock, ImportRunChange>
{
    /// <summary>SQLSTATE de violação de unicidade no PostgreSQL (mesma técnica do OrderRepository).</summary>
    private const string UniqueViolation = "23505";

    /// <summary>Mensagem do contrato para a corrida de execução ativa (FR-016, 409).</summary>
    private const string ACTIVE_RUN_MESSAGE = "Já existe importação em andamento.";

    /// <summary>Mensagem da FR-020 (recuperação de execuções órfãs no startup).</summary>
    private const string INTERRUPTED_MESSAGE = "Execução interrompida pela reinicialização do servidor.";

    /// <summary>Os dois status que contam como "ativa" no índice único parcial (queued, running).</summary>
    private static readonly ImportRunStatus[] ActiveRunStatuses = [ImportRunStatus.Queued, ImportRunStatus.Running];

    /// <summary>Os dois status de bloco que ainda não terminaram (pending, running).</summary>
    private static readonly ImportBlockStatus[] OpenBlockStatuses = [ImportBlockStatus.Pending, ImportBlockStatus.Running];

    private readonly AmbrosioContext _context;

    public ImportRunRepository(AmbrosioContext context)
    {
        _context = context;
    }

    // ------------------------------------------------------------------ reads

    /// <inheritdoc />
    public async Task<ImportRun?> GetByIdAsync(long runId, long userId)
    {
        var run = await _context.ImportRuns
            .AsNoTracking()
            .Where(r => r.UserId == userId)
            .Include(r => r.Blocks)
            .FirstOrDefaultAsync(r => r.Id == runId);

        if (run is not null)
        {
            SortBlocks(run);
        }

        return run;
    }

    /// <inheritdoc />
    public async Task<ImportRun?> GetCurrentAsync(long userId)
    {
        var run = await _context.ImportRuns
            .AsNoTracking()
            .Where(r => r.UserId == userId)
            .Include(r => r.Blocks)
            .Where(r => r.Status == ImportRunStatus.Queued || r.Status == ImportRunStatus.Running)
            .OrderBy(r => r.RequestedAt)
            .FirstOrDefaultAsync();

        if (run is not null)
        {
            SortBlocks(run);
        }

        return run;
    }

    /// <inheritdoc />
    public async Task<(List<ImportRun> Items, int TotalCount)> ListAsync(long userId, int page, int pageSize)
    {
        var scoped = _context.ImportRuns
            .AsNoTracking()
            .Where(r => r.UserId == userId);

        var totalCount = await scoped.CountAsync();

        var items = await scoped
            // ix_import_runs_user_requested (user_id, requested_at DESC) atende filtro e ordenação.
            .OrderByDescending(r => r.RequestedAt)
            .ThenByDescending(r => r.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    /// <inheritdoc />
    public async Task<List<ImportRunChange>> ListChangesAsync(long runId, long userId) =>
        // A linha de mudança não tem user_id própria: o filtro é pela EXECUÇÃO (FR-048) e vem antes de
        // qualquer outra cláusula (§VI). Uma execução de outra vendedora devolve lista vazia — e o
        // serviço já respondeu 404 antes de chegar aqui.
        await _context.ImportRunChanges
            .AsNoTracking()
            .Where(c => c.ImportRun!.UserId == userId && c.ImportRunId == runId)
            .OrderBy(c => c.ImportRunBlockId)
            .ThenBy(c => c.Id)
            .ToListAsync();

    /// <inheritdoc />
    public async Task<List<ImportRunBreakdownInfo>> GetBreakdownAsync(long runId, long userId)
    {
        var rows = await _context.ImportRunChanges
            .AsNoTracking()
            .Where(c => c.ImportRun!.UserId == userId && c.ImportRunId == runId)
            .GroupBy(c => new { c.Channel, c.Origin, c.Kind, c.Action })
            .Select(g => new ImportRunBreakdownInfo
            {
                Channel = g.Key.Channel,
                Origin = g.Key.Origin,
                Kind = g.Key.Kind,
                Action = g.Key.Action,
                Count = g.Count()
            })
            .ToListAsync();

        // Canal e origem primeiro, depois tipo e ação: é a ordem das colunas do relatório na tela.
        return [.. rows
            .OrderBy(r => r.Channel)
            .ThenBy(r => r.Origin)
            .ThenBy(r => r.Kind)
            .ThenBy(r => r.Action)];
    }

    /// <inheritdoc />
    public async Task<int?> GetPreviousRunOrdersCountAsync(long userId, long runId, DateOnly startDate, DateOnly endDate)
    {
        var previous = await _context.ImportRuns
            .AsNoTracking()
            .Where(r => r.UserId == userId)
            .Where(r => r.StartDate == startDate && r.EndDate == endDate)
            .Where(r => r.Id != runId)
            .Where(r => r.Mode == ImportRunMode.Apply)
            .Where(r => r.Status == ImportRunStatus.Succeeded || r.Status == ImportRunStatus.Partial)
            // ix_import_runs_user_period: a última execução concluída do MESMO período é a base do
            // aviso de queda (FR-051, research §18).
            .OrderByDescending(r => r.FinishedAt)
            .FirstOrDefaultAsync();

        return previous?.OrdersSeen;
    }

    /// <inheritdoc />
    public async Task<DateOnly?> GetSuggestedStartDateAsync(long userId) =>
        // MAX(order_date) só do que veio do +Vendas, atendido por ix_orders_user_source_date (research §11).
        await _context.Orders
            .AsNoTracking()
            .Where(o => o.UserId == userId)
            .Where(o => o.Source == OrderSource.MaisVendas)
            .Select(o => (DateOnly?)o.OrderDate)
            .MaxAsync();

    /// <inheritdoc />
    public async Task<List<ImportedOrderKey>> ListImportedKeysInRangeAsync(long userId, DateOnly from, DateOnly to) =>
        // Só source = maisVendas: pedido manual nunca é "não encontrado", porque nunca veio da origem
        // (FR-027). Só a chave natural sai daqui — nem cliente nem CPF (FR-052).
        await _context.Orders
            .AsNoTracking()
            .Where(o => o.UserId == userId)
            .Where(o => o.Source == OrderSource.MaisVendas)
            .Where(o => o.OrderDate >= from && o.OrderDate <= to)
            .Select(o => new ImportedOrderKey(o.Id, o.OrderNumber, o.OrderDate, o.Kind, o.Channel, o.Origin))
            .ToListAsync();

    /// <inheritdoc />
    public async Task<Dictionary<string, long>> GetLinkedImageIdsAsync(
        long userId,
        string orderNumber,
        DateOnly orderDate,
        OrderKind kind)
    {
        // FR-040: as fotos JÁ ligadas a este pedido, indexadas pela URL de origem. É o que faz a
        // repetição de um bloco não baixar de novo a imagem do pedido que falhou depois da foto.
        var rows = await _context.Orders
            .AsNoTracking()
            .Where(o => o.UserId == userId)
            .Where(o => o.OrderNumber == orderNumber && o.OrderDate == orderDate && o.Kind == kind)
            .SelectMany(o => o.Items)
            .Where(i => i.ImageId != null && i.ImageUrl != null)
            .Select(i => new { i.ImageUrl, i.ImageId })
            .ToListAsync();

        return rows
            .Where(r => r.ImageUrl is not null)
            .GroupBy(r => r.ImageUrl!)
            .ToDictionary(g => g.Key, g => g.Max(r => r.ImageId!.Value));
    }

    // ------------------------------------------------------------------ writes

    /// <inheritdoc />
    public async Task<ImportRun> InsertAsync(ImportRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        // Os blocos entram junto (travessia de grafo do Add): a tela precisa de "5 blocos" já no 202
        // (FR-009), e um único SaveChanges garante que execução e planejamento nunca existam separados.
        _context.ImportRuns.Add(run);

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex, "ux_import_runs_user_active"))
        {
            // FR-016/SC-008: duas requisições simultâneas da mesma vendedora — o índice único parcial
            // decidiu. A execução que já estava em curso segue intacta.
            throw new DuplicateException(ACTIVE_RUN_MESSAGE);
        }

        SortBlocks(run);

        return run;
    }

    /// <inheritdoc />
    public async Task<ImportRun> UpdateAsync(ImportRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        var existing = await _context.ImportRuns
            .FirstOrDefaultAsync(r => r.Id == run.Id && r.UserId == run.UserId);

        if (existing is null)
        {
            throw new NotFoundException("Execução de importação não encontrada.");
        }

        // Só os escalares: blocos e alterações têm escrita própria (UpdateBlockAsync/AddChangeAsync) —
        // travar o grafo aqui regravar contagens que a execução está atualizando bloco a bloco.
        _context.Entry(existing).CurrentValues.SetValues(run);
        await _context.SaveChangesAsync();

        return existing;
    }

    /// <inheritdoc />
    public async Task<ImportRunBlock> UpdateBlockAsync(ImportRunBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);

        var existing = await _context.ImportRunBlocks
            .FirstOrDefaultAsync(b => b.Id == block.Id);

        if (existing is null)
        {
            throw new NotFoundException("Bloco da importação não encontrado.");
        }

        _context.Entry(existing).CurrentValues.SetValues(block);
        await _context.SaveChangesAsync();

        return existing;
    }

    /// <inheritdoc />
    public async Task<ImportRunChange> AddChangeAsync(ImportRunChange change)
    {
        ArgumentNullException.ThrowIfNull(change);

        _context.ImportRunChanges.Add(change);
        await _context.SaveChangesAsync();

        return change;
    }

    /// <inheritdoc />
    public async Task<int> RequestCancelAsync(long runId, long userId) =>
        // Escrita direcionada: user_id na cláusula, e uma execução de outra vendedora simplesmente não
        // é alterada (0 linhas → o serviço devolve 404 como se não existisse). Só marca o pedido de
        // cancelamento; quem fecha o status é a execução, ao fim do bloco em curso (FR-019).
        await _context.ImportRuns
            .Where(r => r.UserId == userId)
            .Where(r => r.Id == runId)
            .Where(r => r.Status == ImportRunStatus.Queued || r.Status == ImportRunStatus.Running)
            .ExecuteUpdateAsync(setters => setters.SetProperty(r => r.CancelRequested, true));

    /// <inheritdoc />
    public async Task<int> MarkInterruptedAsync(DateTime now)
    {
        // FR-020. SOMENTE ESCRITA EM LOTE por construção: nenhum ToListAsync, nenhum FirstAsync,
        // nenhuma entidade materializada, e o retorno é só a contagem de linhas — assim a recuperação
        // do startup não é um método de leitura e não tem como vazar dado de usuário (constituição §VI).
        // Chamada apenas no arranque do ImportRunWorker (início do ExecuteAsync, antes de ler a fila),
        // nunca por um endpoint (T111 conferido por busca no código).

        // 1) Blocos ainda não terminados das execuções ativas viram notExecuted. A navegação para a
        //    execução vira EXISTS/JOIN dentro do próprio UPDATE: não há janela entre "ler as ativas" e
        //    "escrever nelas", e nada é lido antes.
        var blocks = await _context.ImportRunBlocks
            .Where(b => OpenBlockStatuses.Contains(b.Status))
            .Where(b => b.ImportRun != null && ActiveRunStatuses.Contains(b.ImportRun.Status))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(b => b.Status, ImportBlockStatus.NotExecuted)
                .SetProperty(b => b.ErrorMessage, INTERRUPTED_MESSAGE)
                .SetProperty(b => b.FinishedAt, now));

        // 2) As execuções ativas viram failed com a mensagem padrão do contrato.
        var runs = await _context.ImportRuns
            .Where(r => ActiveRunStatuses.Contains(r.Status))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(r => r.Status, ImportRunStatus.Failed)
                .SetProperty(r => r.ErrorMessage, INTERRUPTED_MESSAGE)
                .SetProperty(r => r.StartedAt, r => r.StartedAt ?? now)
                .SetProperty(r => r.FinishedAt, now));

        return blocks + runs;
    }

    /// <inheritdoc />
    public async Task<ImportRun?> ClaimNextQueuedRunAsync(DateTime now)
    {
        // 1) A fila é a tabela: a execução `queued` mais antiga. Projeção em vez de entidade porque o
        //    worker não precisa de nada dela além da chave, e carregar a execução inteira colocaria no
        //    tracker uma linha que o ExecuteUpdate abaixo nem vê.
        var candidate = await _context.ImportRuns
            .AsNoTracking()
            .Where(r => r.Status == ImportRunStatus.Queued)
            .OrderBy(r => r.RequestedAt)
            .ThenBy(r => r.Id)
            .Select(r => new { r.Id, r.UserId })
            .FirstOrDefaultAsync();

        if (candidate is null)
        {
            return null;
        }

        // 2) Reivindicação atômica: o `WHERE status = queued` é a tranca. Duas varreduras (a desta
        //    réplica no tick seguinte, ou outra réplica algum dia) nunca executam a mesma linha: a
        //    perdedora recebe 0 e tenta a próxima no próximo tick (R9 da 008). A transição já grava
        //    `started_at`, que é o que o relatório usa para o tempo de execução (FR-047).
        var claimed = await _context.ImportRuns
            .Where(r => r.Id == candidate.Id && r.UserId == candidate.UserId)
            .Where(r => r.Status == ImportRunStatus.Queued)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(r => r.Status, ImportRunStatus.Running)
                .SetProperty(r => r.StartedAt, now));

        if (claimed != 1)
        {
            return null;
        }

        // 3) Devolve a linha reivindicada, relida pelo par (id, dono) que a candidate mostrou — sem
        //    blocos nem rastro: o executor relê tudo com o filtro de usuário antes de escrever.
        return await _context.ImportRuns
            .AsNoTracking()
            .Where(r => r.Id == candidate.Id && r.UserId == candidate.UserId)
            .FirstOrDefaultAsync();
    }

    // ------------------------------------------------------- pedido importado (upsert)

    /// <inheritdoc />
    public async Task<UpsertOutcome> UpsertImportedOrderAsync(
        long userId,
        CollectedOrder snapshot,
        string cpfHash,
        string cpfEncrypted,
        string cpfMasked,
        string sourceHash,
        IReadOnlyDictionary<string, long> imageIdsByUrl,
        DateTime now)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        // Transação PRÓPRIA, curta, por pedido (research §7): cliente + pedido + itens entram juntos ou
        // não entram, e uma falha aqui não desfaz nada já gravado no bloco.
        await using var transaction = await _context.Database.BeginTransactionAsync();

        var (customer, customerCreated) = await ResolveCustomerAsync(
            userId, snapshot, cpfHash, cpfEncrypted, cpfMasked, now);

        var existing = await FindByKeyAsync(userId, snapshot);

        ImportChangeAction action;
        long orderId;
        List<string>? changedFields = null;

        if (existing is null)
        {
            var order = Order.CreateImported(userId, customer.Id, snapshot, sourceHash, now);
            order.ApplyImageIds(imageIdsByUrl);

            _context.Orders.Add(order);
            await _context.SaveChangesAsync();

            action = ImportChangeAction.Inserted;
            orderId = order.Id;
        }
        else if (existing.Source == OrderSource.Manual)
        {
            // FR-028: a chave natural já pertence a um pedido digitado à mão. Não se toca nele — o
            // conflito é apenas reportado, e a vendedora decide o que fazer com os dois.
            action = ImportChangeAction.Conflict;
            orderId = existing.Id;
        }
        else if (string.Equals(existing.SourceHash, sourceHash, StringComparison.Ordinal))
        {
            existing.Touch(now);
            await _context.SaveChangesAsync();

            action = ImportChangeAction.Unchanged;
            orderId = existing.Id;
        }
        else
        {
            // Hash diferente, OU hash nulo: pedido importado editado à mão (o OrderService zera o hash
            // na edição — T074a), então a próxima reimportação volta a aplicar a origem (FR-023).
            // As linhas antigas são substituídas pela lista da origem. Tirá-las da coleção não basta: a
            // FK order_items.order_id é obrigatória com ClientSetNull (constituição §V, nunca cascata),
            // e o EF recusaria "desligar" o item do pedido. Elas são marcadas como excluídas aqui, e o EF
            // ordena esses DELETEs antes dos INSERTs que reutilizam (order_id, line_no).
            var previousItems = existing.Items.ToList();

            changedFields = existing.ApplyImported(snapshot, sourceHash, now);
            existing.ApplyImageIds(imageIdsByUrl);

            _context.OrderItems.RemoveRange(previousItems);

            await _context.SaveChangesAsync();

            action = ImportChangeAction.Updated;
            orderId = existing.Id;
        }

        await transaction.CommitAsync();

        return new UpsertOutcome
        {
            Action = action,
            OrderId = orderId,
            ChangedFields = changedFields,
            CustomerCreated = customerCreated
        };
    }

    /// <inheritdoc />
    public async Task<UpsertOutcome> PreviewImportedOrderAsync(
        long userId,
        CollectedOrder snapshot,
        string cpfHash,
        string sourceHash)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        // Só leitura, sem transação e sem Add: a simulação tem de dizer "o que eu faria" sem tocar uma
        // linha (FR-032, SC-011). As decisões são as MESMAS do upsert — por isso os dois caminhos
        // compartilham FindByKeyAsync, a comparação de hash e o diff campo a campo.
        var customerExists = await _context.Customers
            .AsNoTracking()
            .Where(c => c.UserId == userId)
            .AnyAsync(c => c.CpfHash == cpfHash);

        var existing = await _context.Orders
            .AsNoTracking()
            .Where(o => o.UserId == userId)
            .Include(o => o.Customer)
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.OrderNumber == Normalize(snapshot.OrderNumber)
                                      && o.OrderDate == snapshot.OrderDate
                                      && o.Kind == snapshot.Kind);

        if (existing is null)
        {
            return new UpsertOutcome
            {
                Action = ImportChangeAction.Inserted,
                OrderId = null,
                CustomerCreated = !customerExists
            };
        }

        if (existing.Source == OrderSource.Manual)
        {
            return new UpsertOutcome { Action = ImportChangeAction.Conflict, OrderId = existing.Id };
        }

        if (string.Equals(existing.SourceHash, sourceHash, StringComparison.Ordinal))
        {
            return new UpsertOutcome { Action = ImportChangeAction.Unchanged, OrderId = existing.Id };
        }

        var changed = SourceHashCalculator.DiffFields(
            existing.ToSnapshot(),
            snapshot,
            existing.Customer?.CpfHash,
            cpfHash);

        return new UpsertOutcome
        {
            Action = ImportChangeAction.Updated,
            OrderId = existing.Id,
            ChangedFields = changed,
            CustomerCreated = !customerExists
        };
    }

    /// <summary>
    /// Cliente do pedido: busca por <c>cpf_hash</c> (o documento nunca é comparado em claro), cria sob
    /// demanda com hash/cifra/máscara (FR-034) ou atualiza <b>só o nome</b> quando a origem mudou o
    /// nome, preservando todo o resto que a vendedora digitou (FR-035).
    /// </summary>
    /// <returns>O cliente e <c>true</c> quando um novo foi cadastrado agora.</returns>
    private async Task<(Customer Customer, bool Created)> ResolveCustomerAsync(
        long userId,
        CollectedOrder snapshot,
        string cpfHash,
        string cpfEncrypted,
        string cpfMasked,
        DateTime now)
    {
        var customer = await _context.Customers
            .FirstOrDefaultAsync(c => c.UserId == userId && c.CpfHash == cpfHash);

        if (customer is not null)
        {
            var incomingName = Normalize(snapshot.CustomerName);

            // Só o nome: as três colunas de CPF ficam intocadas e nenhum outro campo é lido ou escrito.
            if (incomingName is { Length: > 0 }
                && !string.Equals(customer.Name, incomingName, StringComparison.Ordinal))
            {
                customer.UpdateName(incomingName, now);
                await _context.SaveChangesAsync();
            }

            return (customer, Created: false);
        }

        customer = new Customer
        {
            UserId = userId,
            Name = Normalize(snapshot.CustomerName) ?? "Cliente do +Vendas",
            CpfHash = cpfHash,
            CpfEncrypted = cpfEncrypted,
            CpfMasked = cpfMasked,
            CreatedAt = now,
            UpdatedAt = now
        };

        _context.Customers.Add(customer);

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex, "ux_customers_user_cpf"))
        {
            // Corrida (research §7): outra execução da mesma vendedora cadastrou o mesmo CPF entre a
            // nossa leitura e o INSERT. O índice único decidiu o vencedor; desanexa o nosso e relê —
            // uma única vez, porque a segunda corrida seria outro problema (e não acontece aqui: a
            // linha vencedora já existe e estável).
            var winner = await _context.Customers
                .FirstAsync(c => c.UserId == userId && c.CpfHash == cpfHash);

            _context.Entry(customer).State = EntityState.Detached;

            return (winner, Created: false);
        }

        return (customer, Created: true);
    }

    /// <summary>Busca pela chave natural com cliente e linhas, para decisão e diff.</summary>
    private async Task<Order?> FindByKeyAsync(long userId, CollectedOrder snapshot) =>
        await _context.Orders
            .Where(o => o.UserId == userId)
            .Include(o => o.Customer)
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.OrderNumber == Normalize(snapshot.OrderNumber)
                                      && o.OrderDate == snapshot.OrderDate
                                      && o.Kind == snapshot.Kind);

    private static void SortBlocks(ImportRun run) =>
        run.Blocks = [.. run.Blocks.OrderBy(b => b.BlockNo)];

    /// <summary>Mesma normalização do modelo: a chave natural é comparada sem espaços nas pontas.</summary>
    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// <c>23505</c> com o nome da constraint conferido quando informado — é o que distingue "duas
    /// execuções ativas" de "dois pedidos com a mesma chave natural" sem comparar texto de mensagem.
    /// </summary>
    private static bool IsUniqueViolation(Exception ex, string constraintName) =>
        ex is DbUpdateException dbUpdate
        && dbUpdate.InnerException is PostgresException postgres
        && postgres.SqlState == UniqueViolation
        && string.Equals(postgres.ConstraintName, constraintName, StringComparison.Ordinal);
}
