using System.Globalization;
using Ambrosio.DTO.Customers;
using Ambrosio.DTO.Enums;
using Ambrosio.DTO.Orders;
using FluentValidation;
using FluentValidation.Results;

namespace Ambrosio.McpServer.Mcp.Results;

/// <summary>
/// Formato pt-BR das tools de leitura (feature 007, contracts/mcp-tools.md): os mesmos dados das
/// respostas REST com nomes em português, CPF sempre mascarado (o detalhe REST traz o CPF completo, e
/// ele nunca passa por aqui) e o tipo do pedido como <c>venda</c>/<c>devolucao</c>/<c>cancelamento</c>.
/// </summary>
public static class ToolMapping
{
    public static object Customer(CustomerInfo customer) => new
    {
        id = customer.Id,
        nome = customer.Name,
        cpfMasked = customer.CpfMasked
    };

    public static object CustomerDetail(CustomerDetailInfo customer) => new
    {
        id = customer.Id,
        nome = customer.Name,
        cpfMasked = customer.CpfMasked,
        resumo = Summary(customer.Summary)
    };

    public static object Summary(CustomerSummaryInfo summary) => new
    {
        compras = summary.Purchases,
        totalLiquido = summary.NetTotal,
        ultimaCompra = IsoDate(summary.LastPurchase),
        diasSemComprar = summary.DaysSinceLastPurchase
    };

    public static object Order(OrderInfo order) => new
    {
        id = order.Id,
        clienteId = order.CustomerId,
        cliente = order.CustomerName,
        numero = order.OrderNumber,
        data = IsoDate(order.OrderDate),
        tipo = KindName(order.Kind),
        canal = order.Channel,
        origem = order.Origin?.ToString().ToLowerInvariant(),
        status = order.Status,
        total = order.Total,
        fonte = order.Source.ToString().ToLowerInvariant(),
        qtdItens = order.ItemsCount
    };

    public static object OrderDetail(OrderDetailInfo order) => new
    {
        id = order.Id,
        cliente = new
        {
            id = order.Customer.Id,
            nome = order.Customer.Name,
            cpfMasked = order.Customer.CpfMasked
        },
        numero = order.OrderNumber,
        data = IsoDate(order.OrderDate),
        tipo = KindName(order.Kind),
        canal = order.Channel,
        origem = order.Origin?.ToString().ToLowerInvariant(),
        status = order.Status,
        total = order.Total,
        fonte = order.Source.ToString().ToLowerInvariant(),
        vendaOriginalEm = IsoDate(order.OriginalSaleOn),
        canceladoEm = IsoDate(order.CancelledOn),
        capturado_em = order.CapturedAt?.ToString("O", CultureInfo.InvariantCulture),
        itens = order.Items.Select(Item).ToList()
    };

    public static object Item(OrderItemInfo item) => new
    {
        linha = item.LineNo,
        produto = item.ProductName,
        referencia = item.SkuRef,
        tamanho = item.Size,
        preco = item.Price,
        imagemId = item.ImageId
    };

    public static string KindName(OrderKind kind) => kind switch
    {
        OrderKind.Sale => "venda",
        OrderKind.Return => "devolucao",
        OrderKind.Cancellation => "cancelamento",
        _ => kind.ToString().ToLowerInvariant()
    };

    /// <summary>Tipo em pt-BR para o enum da API; nulo se vazio.</summary>
    public static OrderKind? ParseKind(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        null or "" => null,
        "venda" => OrderKind.Sale,
        "devolucao" or "devolução" => OrderKind.Return,
        "cancelamento" => OrderKind.Cancellation,
        _ => throw Invalid("tipo", "O tipo deve ser venda, devolucao ou cancelamento.")
    };

    /// <summary>Data <c>AAAA-MM-DD</c> de argumento; nula se vazia.</summary>
    public static DateOnly? ParseDate(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateOnly.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : throw Invalid(field, $"A data {field} deve estar no formato AAAA-MM-DD.");
    }

    public static string? IsoDate(DateOnly? value) => value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static ValidationException Invalid(string field, string message) =>
        new([new ValidationFailure(field, message)]);
}
