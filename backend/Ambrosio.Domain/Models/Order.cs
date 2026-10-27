using Ambrosio.Domain.Common;
using Ambrosio.DTO.Enums;
using Ambrosio.DTO.Imports.Collector;

namespace Ambrosio.Domain.Models;

/// <summary>
/// Venda, devolução ou cancelamento de um cliente (tabela <c>orders</c>). Unicidade por
/// <c>(user_id, order_number, order_date, kind)</c> (FR-014). O <c>user_id</c> é redundante em
/// relação ao cliente, por projeto, para permitir isolamento e índices sem join (data-model).
/// </summary>
public class Order
{
    public long Id { get; set; }

    /// <summary>Dono do registro. Vem exclusivamente do claim <c>sub</c> do token (FR-021).</summary>
    public long UserId { get; set; }

    public long CustomerId { get; set; }

    /// <summary>Número de origem (ex.: <c>00004751</c>, <c>v1161148crb-01</c>). Não é único sozinho.</summary>
    public string OrderNumber { get; set; } = string.Empty;

    public DateOnly OrderDate { get; set; }

    /// <summary>Imutável após a criação: um cancelamento é um novo pedido com total negativo.</summary>
    public OrderKind Kind { get; set; }

    /// <summary>Canal de origem (Linx, Online, Vitrine, …). Texto livre — valores novos são aceitos.</summary>
    public string? Channel { get; set; }

    public OrderOrigin? Origin { get; set; }

    /// <summary>Status de origem (Faturado, Cancelado, …). Texto livre e atualizável (FR-019).</summary>
    public string? Status { get; set; }

    /// <summary>&gt; 0 em venda; &lt; 0 em devolução/cancelamento; nunca 0 (FR-013).</summary>
    public decimal Total { get; set; }

    /// <summary>Cancelamento: data da venda original.</summary>
    public DateOnly? OriginalSaleOn { get; set; }

    /// <summary>Cancelamento: data do cancelamento.</summary>
    public DateOnly? CancelledOn { get; set; }

    /// <summary>Identificador externo (4º segmento da URL do +Vendas).</summary>
    public string? ExternalId { get; set; }

    /// <summary>
    /// Como o pedido entrou no Ambrosio (feature 006). Padrão <see cref="OrderSource.Manual"/>: as
    /// linhas anteriores à migração continuam manuais sem backfill (data-model §1).
    /// </summary>
    public OrderSource Source { get; set; } = OrderSource.Manual;

    /// <summary>
    /// SHA-256 do conteúdo normalizado vindo da origem (research §6). É o que decide "inalterado" vs
    /// "atualizado". Nulo em pedido manual e num pedido importado editado à mão — o
    /// <c>OrderService</c> zera o hash na edição (FR-023), o que faz a reimportação reverter o valor.
    /// </summary>
    public string? SourceHash { get; set; }

    /// <summary>Última captura em que este pedido foi visto na origem (FR-030).</summary>
    public DateTime? CapturedAt { get; set; }

    /// <summary>Última vez em que o <see cref="SourceHash"/> mudou (FR-030).</summary>
    public DateTime? SourceChangedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public Customer? Customer { get; set; }

    public User? User { get; set; }

    public List<OrderItem> Items { get; set; } = [];

    /// <summary>
    /// Sinal do total compatível com o tipo (FR-013). Delegado a
    /// <see cref="OrderTotalSignRule"/>, compartilhado com validator e serviço.
    /// </summary>
    public static bool IsTotalSignValid(OrderKind kind, decimal total) =>
        OrderTotalSignRule.IsValid(kind, total);

    /// <summary>
    /// Adiciona uma linha numerando-a sequencialmente a partir de 1 (FR-018). O número da linha é
    /// decisão do agregado: quem monta o pedido não escolhe <c>LineNo</c>.
    /// </summary>
    public void AddItem(OrderItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        item.LineNo = Items.Count + 1;
        Items.Add(item);
    }

    /// <summary>
    /// Pedido vindo da importação do +Vendas (feature 006, FR-020/FR-030). Nasce já com a origem
    /// marcada, o hash do conteúdo e as duas datas de rastreamento iguais — é a primeira vez que a
    /// origem é vista para esta chave natural.
    /// </summary>
    /// <param name="userId">Dono, do claim <c>sub</c> da execução.</param>
    /// <param name="customerId">Cliente já resolvido pelo CPF (criado sob demanda, FR-034).</param>
    /// <param name="snapshot">Pedido normalizado vindo do coletor.</param>
    /// <param name="sourceHash">SHA-256 hex do conteúdo (research §6).</param>
    /// <param name="now">Instante UTC de <see cref="Interfaces.IClock"/>.</param>
    public static Order CreateImported(
        long userId,
        long customerId,
        CollectedOrder snapshot,
        string sourceHash,
        DateTime now)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var order = new Order
        {
            UserId = userId,
            CustomerId = customerId,
            OrderNumber = NormalizeText(snapshot.OrderNumber) ?? string.Empty,
            OrderDate = snapshot.OrderDate,
            Kind = snapshot.Kind,
            Channel = NormalizeText(snapshot.Channel),
            Origin = snapshot.Origin,
            Status = NormalizeText(snapshot.Status),
            Total = snapshot.Total,
            OriginalSaleOn = snapshot.OriginalSaleOn,
            CancelledOn = snapshot.CancelledOn,
            ExternalId = NormalizeText(snapshot.ExternalId),
            Source = OrderSource.MaisVendas,
            SourceHash = sourceHash,
            CapturedAt = now,
            SourceChangedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };

        // As linhas seguem a numeração da origem, e Order.AddItem mantém a sequência 1-based (FR-018).
        foreach (var item in snapshot.Items.OrderBy(i => i.LineNo))
        {
            order.AddItem(new OrderItem
            {
                ProductName = NormalizeText(item.ProductName),
                SkuRef = NormalizeText(item.SkuRef),
                Size = NormalizeText(item.Size),
                Price = item.Price,
                ImageUrl = NormalizeText(item.ImageUrl)
            });
        }

        return order;
    }

    /// <summary>
    /// Liga as linhas às imagens já disponíveis no storage da vendedora (feature 006, FR-037/FR-040):
    /// cada linha recebe a imagem cuja URL de origem bate com a sua. Não muda o conteúdo vindo da
    /// origem, por isso não afeta o <see cref="SourceHash"/> — que deliberadamente ignora
    /// <c>image_id</c> (research §6).
    /// </summary>
    /// <param name="imageIdsByUrl">Mapa <c>imageUrl → imageId</c> das fotos desta execução.</param>
    /// <returns>Quantas linhas ficaram ligadas a uma imagem.</returns>
    public int ApplyImageIds(IReadOnlyDictionary<string, long> imageIdsByUrl)
    {
        ArgumentNullException.ThrowIfNull(imageIdsByUrl);

        var linked = 0;

        foreach (var item in Items)
        {
            if (item.ImageUrl is { Length: > 0 } url
                && imageIdsByUrl.TryGetValue(url, out var imageId)
                && imageId > 0)
            {
                item.ImageId = imageId;
                linked++;
            }
        }

        return linked;
    }

    /// <summary>
    /// Reencontro do mesmo conteúdo na origem: só <see cref="CapturedAt"/> muda, porque o pedido não
    /// foi alterado (FR-021, <c>unchanged</c>). <see cref="UpdatedAt"/> fica quieto de propósito —
    /// mexer nele faria toda reimportação parecer edição.
    /// </summary>
    /// <param name="now">Instante UTC de <see cref="Interfaces.IClock"/>.</param>
    public void Touch(DateTime now) => CapturedAt = now;

    /// <summary>
    /// Sobrescreve os campos vindos da origem sobre um pedido já importado e devolve a lista de campos
    /// alterados para o relatório (FR-023, clarificação 2 da spec: a origem prevalece).
    /// <para>
    /// As linhas são substituídas pela lista da origem, mas o <see cref="OrderItem.ImageId"/> de uma
    /// linha que manteve a mesma <c>image_url</c> é preservado — a foto já copiada para o storage não
    /// é perdida nem baixada de novo (FR-040/FR-041). Campos que só existem no Ambrosio
    /// (<see cref="CreatedAt"/>, <see cref="Customer"/> do mesmo CPF) ficam intactos.
    /// </para>
    /// </summary>
    /// <param name="snapshot">Pedido normalizado vindo do coletor.</param>
    /// <param name="sourceHash">Hash do conteúdo que está chegando.</param>
    /// <param name="now">Instante UTC de <see cref="Interfaces.IClock"/>.</param>
    /// <returns>Nomes dos campos alterados (<c>total</c>, <c>status</c>, <c>items</c>…); vazia quando nada mudou.</returns>
    public List<string> ApplyImported(CollectedOrder snapshot, string sourceHash, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var changed = SourceHashCalculator.DiffFields(ToSnapshot(), snapshot, Customer?.CpfHash, Customer?.CpfHash);

        Channel = NormalizeText(snapshot.Channel);
        Origin = snapshot.Origin;
        Status = NormalizeText(snapshot.Status);
        Total = snapshot.Total;
        OriginalSaleOn = snapshot.OriginalSaleOn;
        CancelledOn = snapshot.CancelledOn;
        ExternalId = NormalizeText(snapshot.ExternalId);

        // A foto ligada por URL de origem sobrevive à substituição das linhas (FR-040).
        var keptImagesBySourceUrl = Items
            .Where(i => i.ImageId is not null && i.ImageUrl is not null)
            .GroupBy(i => i.ImageUrl!)
            .ToDictionary(g => g.Key, g => g.First().ImageId!.Value);

        Items.Clear();

        foreach (var item in snapshot.Items.OrderBy(i => i.LineNo))
        {
            var line = new OrderItem
            {
                OrderId = Id,
                ProductName = NormalizeText(item.ProductName),
                SkuRef = NormalizeText(item.SkuRef),
                Size = NormalizeText(item.Size),
                Price = item.Price,
                ImageUrl = NormalizeText(item.ImageUrl)
            };

            if (line.ImageUrl is { Length: > 0 } url && keptImagesBySourceUrl.TryGetValue(url, out var imageId))
            {
                line.ImageId = imageId;
            }

            Items.Add(line);
        }

        for (var i = 0; i < Items.Count; i++)
        {
            Items[i].LineNo = i + 1;
        }

        Source = OrderSource.MaisVendas;
        SourceHash = sourceHash;
        CapturedAt = now;
        SourceChangedAt = now;
        UpdatedAt = now;

        return changed;
    }

    /// <summary>O pedido gravado como snapshot da origem, para o diff campo a campo (FR-023).</summary>
    public CollectedOrder ToSnapshot() => new()
    {
        OrderNumber = OrderNumber,
        OrderDate = OrderDate,
        Kind = Kind,
        Channel = Channel,
        Origin = Origin,
        Status = Status,
        Total = Total,
        OriginalSaleOn = OriginalSaleOn,
        CancelledOn = CancelledOn,
        ExternalId = ExternalId,
        CustomerName = Customer?.Name ?? string.Empty,
        Items = Items
            .OrderBy(i => i.LineNo)
            .Select(i => new CollectedOrderItem
            {
                LineNo = i.LineNo,
                ProductName = i.ProductName,
                SkuRef = i.SkuRef,
                Size = i.Size,
                Price = i.Price ?? 0m,
                ImageUrl = i.ImageUrl
            })
            .ToList()
    };

    /// <summary>Texto de origem sem espaços nas pontas; vazio vira nulo (a coluna tem limite).</summary>
    private static string? NormalizeText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// Atualiza status e total (FR-019). O sinal é revalidado contra o <see cref="Kind"/>
    /// <b>gravado</b> — <c>kind</c> é imutável — e nada é aplicado quando a regra falha, para não
    /// deixar o pedido com total incompatível com o tipo.
    /// </summary>
    /// <exception cref="ArgumentException">Total com sinal incompatível com o tipo (FR-013).</exception>
    public void UpdateStatusAndTotal(string? status, decimal total, DateTime now)
    {
        if (!IsTotalSignValid(Kind, total))
        {
            throw new ArgumentException(
                OrderTotalSignRule.ErrorMessage(Kind), nameof(total));
        }

        Status = string.IsNullOrWhiteSpace(status) ? null : status.Trim();
        Total = total;
        UpdatedAt = now;
    }
}
