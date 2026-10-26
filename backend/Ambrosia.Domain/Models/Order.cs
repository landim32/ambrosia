using Ambrosia.Domain.Common;
using Ambrosia.DTO.Enums;

namespace Ambrosia.Domain.Models;

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
