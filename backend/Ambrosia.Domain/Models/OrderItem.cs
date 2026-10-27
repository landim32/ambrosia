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

    /// <summary>
    /// Imagem do storage da vendedora copiada desta URL de origem na importação (feature 006,
    /// FR-037). Nula em pedido manual, em item sem foto e quando o download falhou ou o tipo foi
    /// recusado (FR-039) — nesses casos o pedido continua gravado. FK com
    /// <see cref="Microsoft.EntityFrameworkCore.DeleteBehavior.ClientSetNull"/>: apagar a imagem não
    /// apaga o item.
    /// </summary>
    public long? ImageId { get; set; }

    public Order? Order { get; set; }

    /// <summary>Navegação da foto importada; só existe em leitura (o upsert resolve por URL).</summary>
    public StoredImage? Image { get; set; }
}
