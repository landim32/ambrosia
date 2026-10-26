using System.Text.Json.Serialization;

namespace Ambrosia.DTO.Customers;

/// <summary>
/// Resumo de compras do cliente (FR-009), calculado por agregação sobre <c>orders</c>
/// (research §10 — mesma semântica da <c>v_customer_summary</c> do doc 02).
/// </summary>
public class CustomerSummaryInfo
{
    /// <summary>Quantidade de pedidos do tipo <c>sale</c>.</summary>
    [JsonPropertyName("purchases")]
    public int Purchases { get; set; }

    /// <summary>Soma dos totais; devoluções e cancelamentos já vêm negativos.</summary>
    [JsonPropertyName("netTotal")]
    public decimal NetTotal { get; set; }

    /// <summary>Data da última venda (null se nunca comprou).</summary>
    [JsonPropertyName("lastPurchase")]
    public DateOnly? LastPurchase { get; set; }

    /// <summary>Dias entre a última venda e hoje (null se nunca comprou).</summary>
    [JsonPropertyName("daysSinceLastPurchase")]
    public int? DaysSinceLastPurchase { get; set; }
}
