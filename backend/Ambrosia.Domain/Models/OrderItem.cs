namespace Ambrosia.Domain.Models;

/// <summary>
/// Linha de um pedido (tabela <c>order_items</c>). <see cref="LineNo"/> é sequencial a partir de 1
/// dentro do pedido e único por <c>(order_id, line_no)</c>.
/// </summary>
public class OrderItem
{
    public long Id { get; set; }

    public long OrderId { get; set; }

    /// <summary>Número da linha (1-based), atribuído por <c>Order.AddItem</c> (FR-018).</summary>
    public int LineNo { get; set; }

    public string? ProductName { get; set; }

    public string? SkuRef { get; set; }

    /// <summary>Tamanho declarado na origem (ex.: <c>38</c>, <c>P</c>).</summary>
    public string? Size { get; set; }

    /// <summary>Preço unitário; negativo em devolução/cancelamento. Sem validação de sinal.</summary>
    public decimal? Price { get; set; }

    /// <summary>URL da imagem de origem, apenas referência.</summary>
    public string? ImageUrl { get; set; }

    public Order? Order { get; set; }
}
