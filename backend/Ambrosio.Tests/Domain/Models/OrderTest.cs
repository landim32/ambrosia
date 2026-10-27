using Ambrosio.Domain.Models;
using Ambrosio.DTO.Enums;

namespace Ambrosio.Tests.Domain.Models;

/// <summary>
/// T071 [US3]: comportamento do agregado <see cref="Order"/> — numeração sequencial de itens
/// (FR-018), regra de sinal do total por tipo (FR-013) e atualização de status/total que não
/// corrompe o pedido quando o sinal é incompatível (FR-019).
/// </summary>
public class OrderTest
{
    private static readonly DateTime NOW = new(2026, 10, 25, 12, 0, 0, DateTimeKind.Unspecified);

    private static Order NewOrder(OrderKind kind = OrderKind.Sale, decimal total = 100m) => new()
    {
        Id = 1,
        UserId = 7,
        CustomerId = 11,
        OrderNumber = "00004751",
        OrderDate = new DateOnly(2026, 10, 24),
        Kind = kind,
        Total = total,
        Status = "Faturado",
        CreatedAt = NOW.AddDays(-1),
        UpdatedAt = NOW.AddDays(-1)
    };

    private static OrderItem NewItem(string name) => new() { ProductName = name };

    [Fact]
    public void AddItem_ShouldNumberTheLinesSequentiallyFromOne()
    {
        // Arrange
        var order = NewOrder();

        // Act: FR-018 — a numeração é responsabilidade do modelo, não do chamador.
        order.AddItem(NewItem("Calça Inez"));
        order.AddItem(NewItem("Blusa Lia"));
        order.AddItem(NewItem("Cinto Eco"));

        // Assert
        Assert.Equal(3, order.Items.Count);
        Assert.Equal(new[] { 1, 2, 3 }, order.Items.Select(i => i.LineNo).ToArray());
        Assert.Equal(
            new[] { "Calça Inez", "Blusa Lia", "Cinto Eco" },
            order.Items.Select(i => i.ProductName).ToArray());
    }

    [Fact]
    public void AddItem_ShouldRestartTheNumbering_ForEachOrder()
    {
        // Arrange: dois pedidos independentes não compartilham contador (o LineNo é por pedido).
        var first = NewOrder();
        var second = NewOrder();

        // Act
        first.AddItem(NewItem("A"));
        first.AddItem(NewItem("B"));
        second.AddItem(NewItem("C"));

        // Assert
        Assert.Equal(2, first.Items[^1].LineNo);
        Assert.Equal(1, second.Items[^1].LineNo);
    }

    [Fact]
    public void AddItem_ShouldThrow_WhenTheItemIsNull()
    {
        var order = NewOrder();

        Assert.Throws<ArgumentNullException>(() => order.AddItem(null!));
    }

    [Theory]
    [InlineData(OrderKind.Sale, 0.01, true)]
    [InlineData(OrderKind.Sale, 589.80, true)]
    [InlineData(OrderKind.Sale, 0.00, false)]
    [InlineData(OrderKind.Sale, -0.01, false)]
    [InlineData(OrderKind.Sale, -589.80, false)]
    [InlineData(OrderKind.Return, -0.01, true)]
    [InlineData(OrderKind.Return, -30.00, true)]
    [InlineData(OrderKind.Return, 0.00, false)]
    [InlineData(OrderKind.Return, 30.00, false)]
    [InlineData(OrderKind.Cancellation, -589.80, true)]
    [InlineData(OrderKind.Cancellation, 0.00, false)]
    [InlineData(OrderKind.Cancellation, 589.80, false)]
    public void IsTotalSignValid_ShouldEnforceTheSignPerKind(OrderKind kind, decimal total, bool expected)
    {
        // FR-013: venda positiva, devolução/cancelamento negativos, zero nunca.
        Assert.Equal(expected, Order.IsTotalSignValid(kind, total));
    }

    [Fact]
    public void UpdateStatusAndTotal_ShouldThrowAndKeepTheOrder_WhenSignConflictsWithKind()
    {
        // Arrange: um pedido de venda não pode virar negativo pela API de atualização.
        var order = NewOrder(OrderKind.Sale, 589.80m);

        // Act & Assert
        Assert.Throws<ArgumentException>(
            () => order.UpdateStatusAndTotal("Cancelado", -589.80m, NOW));

        // O estado permanece o mesmo: nada é aplicado antes da validação.
        Assert.Equal("Faturado", order.Status);
        Assert.Equal(589.80m, order.Total);
        Assert.Equal(NOW.AddDays(-1), order.UpdatedAt);
    }

    [Theory]
    [InlineData(OrderKind.Sale, 579.80)]
    [InlineData(OrderKind.Return, -25.00)]
    [InlineData(OrderKind.Cancellation, -589.80)]
    public void UpdateStatusAndTotal_ShouldApplyTheValues_WhenSignIsValid(OrderKind kind, decimal total)
    {
        // Arrange
        var order = NewOrder(kind, kind == OrderKind.Sale ? 100m : -100m);

        // Act: FR-019.
        order.UpdateStatusAndTotal("Faturado", total, NOW);

        // Assert
        Assert.Equal("Faturado", order.Status);
        Assert.Equal(total, order.Total);
        Assert.Equal(NOW, order.UpdatedAt);
    }

    [Fact]
    public void UpdateStatusAndTotal_ShouldAcceptNullStatus_KeepingTheTotalValid()
    {
        // status é texto livre e opcional (data-model); o total continua obrigatório.
        var order = NewOrder(OrderKind.Sale, 100m);

        order.UpdateStatusAndTotal(null, 120.50m, NOW);

        Assert.Null(order.Status);
        Assert.Equal(120.50m, order.Total);
    }

    [Fact]
    public void UpdateStatusAndTotal_ShouldRejectZero_ForAnyKind()
    {
        var order = NewOrder(OrderKind.Return, -50m);

        Assert.Throws<ArgumentException>(() => order.UpdateStatusAndTotal("Estornado", 0m, NOW));

        Assert.Equal(-50m, order.Total);
    }

    [Fact]
    public void NoBehaviorMethodChangesKind_BecauseItIsImmutableAfterCreation()
    {
        // Um cancelamento é um NOVO pedido com total negativo (data-model), nunca a mudança de
        // kind de um pedido existente. Os acessores da propriedade continuam públicos (o EF Core
        // materializa a entidade e o serviço define o tipo na criação) — o que não pode existir é
        // um método de comportamento que reatribua o tipo.
        var mutators = typeof(Order)
            .GetMethods()
            .Where(m => m.Name.Contains("Kind", StringComparison.OrdinalIgnoreCase))
            .Where(m => !m.Name.StartsWith("get_", StringComparison.OrdinalIgnoreCase))
            .Where(m => !m.Name.StartsWith("set_", StringComparison.OrdinalIgnoreCase))
            .Select(m => m.Name)
            .ToList();

        Assert.Empty(mutators);
    }

    [Fact]
    public void UpdateStatusAndTotal_CannotReinterpretASaleAsACancellation()
    {
        // SC-006: o tipo gravado manda no sinal aceito; tentar "transformar" uma venda em
        // cancelamento pela atualização é recusado e o kind permanece Sale.
        var order = NewOrder(OrderKind.Sale, 589.80m);

        Assert.Throws<ArgumentException>(() => order.UpdateStatusAndTotal("Cancelado", -589.80m, NOW));

        Assert.Equal(OrderKind.Sale, order.Kind);
    }
}
