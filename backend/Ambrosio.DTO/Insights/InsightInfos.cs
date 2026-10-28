using System.Text.Json.Serialization;
using Ambrosio.DTO.Customers;
using Ambrosio.DTO.Orders;

namespace Ambrosio.DTO.Insights;

/// <summary>Filtros de clientes inativos (feature 007, data-model §4).</summary>
public class InactiveCustomersQuery
{
    /// <summary>Dias mínimos desde a última compra (≥ 1).</summary>
    [JsonPropertyName("days")]
    public int Days { get; set; }

    [JsonPropertyName("minPurchases")]
    public int? MinPurchases { get; set; }

    [JsonPropertyName("minTotal")]
    public decimal? MinTotal { get; set; }

    /// <summary>Teto de linhas devolvidas (máximo 200).</summary>
    [JsonPropertyName("limit")]
    public int Limit { get; set; } = 200;
}

/// <summary>Cliente inativo: resumo de compras e dias desde a última venda.</summary>
public class InactiveCustomerInfo
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("cpfMasked")]
    public string CpfMasked { get; set; } = string.Empty;

    [JsonPropertyName("purchases")]
    public int Purchases { get; set; }

    [JsonPropertyName("netTotal")]
    public decimal NetTotal { get; set; }

    [JsonPropertyName("lastPurchase")]
    public DateOnly LastPurchase { get; set; }

    [JsonPropertyName("daysSinceLastPurchase")]
    public int DaysSinceLastPurchase { get; set; }
}

/// <summary>Página de clientes inativos com o total real (a lista é cortada no limite).</summary>
public class InactiveCustomersResult
{
    [JsonPropertyName("items")]
    public List<InactiveCustomerInfo> Items { get; set; } = [];

    [JsonPropertyName("totalCount")]
    public int TotalCount { get; set; }
}

/// <summary>Período do resumo; sem datas vale o mês de cota atual (26 a 25).</summary>
public class PeriodSummaryQuery
{
    [JsonPropertyName("from")]
    public DateOnly? From { get; set; }

    [JsonPropertyName("to")]
    public DateOnly? To { get; set; }
}

/// <summary>
/// Resumo de vendas do período (doc 02, "Regras de negócio"): líquido = soma dos totais (devolução e
/// cancelamento já são negativos), atendimentos = vendas, peças = itens das vendas,
/// PA = peças / atendimentos e VA = líquido / atendimentos.
/// </summary>
public class PeriodSummaryInfo
{
    [JsonPropertyName("from")]
    public DateOnly From { get; set; }

    [JsonPropertyName("to")]
    public DateOnly To { get; set; }

    [JsonPropertyName("netTotal")]
    public decimal NetTotal { get; set; }

    [JsonPropertyName("sales")]
    public int Sales { get; set; }

    [JsonPropertyName("pieces")]
    public int Pieces { get; set; }

    /// <summary>Peças por atendimento; nulo sem atendimento.</summary>
    [JsonPropertyName("pa")]
    public decimal? Pa { get; set; }

    /// <summary>Valor por atendimento; nulo sem atendimento.</summary>
    [JsonPropertyName("va")]
    public decimal? Va { get; set; }
}

/// <summary>Contagem e soma de um tipo de pedido no período.</summary>
public class KindTotalsInfo
{
    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("total")]
    public decimal Total { get; set; }
}

/// <summary>Devoluções e cancelamentos do período.</summary>
public class ReturnsCancellationsInfo
{
    [JsonPropertyName("from")]
    public DateOnly From { get; set; }

    [JsonPropertyName("to")]
    public DateOnly To { get; set; }

    [JsonPropertyName("returns")]
    public KindTotalsInfo Returns { get; set; } = new();

    [JsonPropertyName("cancellations")]
    public KindTotalsInfo Cancellations { get; set; } = new();
}

/// <summary>Histórico de compras de uma cliente: resumo, ticket médio e pedidos com itens.</summary>
public class CustomerHistoryInfo
{
    [JsonPropertyName("customer")]
    public CustomerInfo Customer { get; set; } = new();

    [JsonPropertyName("summary")]
    public CustomerSummaryInfo Summary { get; set; } = new();

    /// <summary>Líquido / compras; nulo sem compra.</summary>
    [JsonPropertyName("averageTicket")]
    public decimal? AverageTicket { get; set; }

    [JsonPropertyName("orders")]
    public List<OrderDetailInfo> Orders { get; set; } = [];

    [JsonPropertyName("totalOrders")]
    public int TotalOrders { get; set; }
}
