using Ambrosia.Domain.Models;
using Ambrosia.DTO.Enums;
using Ambrosia.DTO.Orders;
using Ambrosia.Infra.Context;
using Ambrosia.Infra.Repository;
using Microsoft.EntityFrameworkCore;

namespace Ambrosia.Tests.Infra.Repository;

/// <summary>
/// T074 [US3]: isolamento, filtros, ordenação e paginação de pedidos sobre
/// <c>UseInMemoryDatabase</c> (research §17) — a cobertura que SC-002/SC-003 exige do
/// repositório antes de qualquer teste HTTP.
/// </summary>
public class OrderRepositoryTest : IDisposable
{
    private const long USER_A = 1;
    private const long USER_B = 2;
    private const long CUSTOMER_A1 = 11;
    private const long CUSTOMER_A2 = 12;

    private static readonly DateTime SEED_TIME =
        new(2026, 10, 1, 8, 0, 0, DateTimeKind.Unspecified);

    private readonly AmbrosiaContext _context;
    private readonly OrderRepository _sut;

    public OrderRepositoryTest()
    {
        // Nome único por instância: os testes xUnit rodam em paralelo.
        _context = new AmbrosiaContext(new DbContextOptionsBuilder<AmbrosiaContext>()
            .UseInMemoryDatabase($"orders-{Guid.NewGuid():N}")
            .Options);

        _sut = new OrderRepository(_context);
    }

    public void Dispose() => _context.Dispose();

    private static Customer NewCustomer(long id, long userId, string name) => new()
    {
        Id = id,
        UserId = userId,
        Name = name,
        CpfHash = $"hash-{id}",
        CpfEncrypted = "cifra",
        CpfMasked = "***.000.000-**",
        CreatedAt = SEED_TIME,
        UpdatedAt = SEED_TIME
    };

    private static Order NewOrder(
        long id,
        long userId,
        long customerId,
        string orderNumber,
        DateOnly orderDate,
        OrderKind kind = OrderKind.Sale,
        string? channel = "Linx",
        decimal total = 100m) => new()
    {
        Id = id,
        UserId = userId,
        CustomerId = customerId,
        OrderNumber = orderNumber,
        OrderDate = orderDate,
        Kind = kind,
        Channel = channel,
        Total = total,
        Status = "Faturado",
        CreatedAt = SEED_TIME,
        UpdatedAt = SEED_TIME
    };

    private static OrderItem NewItem(long id, long orderId, int lineNo, string name, decimal price) => new()
    {
        Id = id,
        OrderId = orderId,
        LineNo = lineNo,
        ProductName = name,
        SkuRef = $"SKU{lineNo}",
        Price = price
    };

    /// <summary>
    /// Corpus padrão: A tem os pedidos 1 (01/10 venda, Linx), 2 (10/10 devolução, Online),
    /// 3 (20/10 cancelamento, "linx" minúsculo); B tem o pedido 4 com o mesmo número do 1.
    /// </summary>
    private async Task SeedStandardCorpus()
    {
        _context.Customers.AddRange(
            NewCustomer(CUSTOMER_A1, USER_A, "Ana Compradora"),
            NewCustomer(CUSTOMER_A2, USER_A, "Bia Cliente"),
            NewCustomer(13, USER_B, "Cliente de Bia"));
        await _context.SaveChangesAsync();

        _context.Orders.AddRange(
            NewOrder(1, USER_A, CUSTOMER_A1, "00004751", new DateOnly(2026, 10, 1),
                OrderKind.Sale, "Linx", 100m),
            NewOrder(2, USER_A, CUSTOMER_A1, "00004752", new DateOnly(2026, 10, 10),
                OrderKind.Return, "Online", -30m),
            NewOrder(3, USER_A, CUSTOMER_A2, "00004753", new DateOnly(2026, 10, 20),
                OrderKind.Cancellation, "linx", -50m),
            NewOrder(4, USER_B, 13, "00004751", new DateOnly(2026, 10, 24),
                OrderKind.Sale, "Linx", 999m));
        await _context.SaveChangesAsync();

        _context.OrderItems.AddRange(
            NewItem(101, 1, 1, "Calça Inez", 100m),
            NewItem(102, 2, 1, "Blusa Lia", -30m),
            NewItem(103, 3, 1, "Cinto Eco", -50m),
            NewItem(104, 4, 1, "Vestido Midi", 999m));
        await _context.SaveChangesAsync();
    }

    private static void AssertIds(IEnumerable<Order> orders, params long[] expected) =>
        Assert.Equal(expected, orders.Select(o => o.Id).ToArray());

    [Fact]
    public async Task ListAsync_ShouldReturnOnlyOrdersOfTheUser()
    {
        // Arrange: FR-021 — 3 pedidos de A, 1 de B.
        await SeedStandardCorpus();

        // Act
        var (items, totalCount) = await _sut.ListAsync(USER_A, new OrderListQuery());

        // Assert
        Assert.Equal(3, totalCount);
        Assert.All(items, o => Assert.Equal(USER_A, o.UserId));
        Assert.DoesNotContain(items, o => o.Total == 999m);
    }

    [Fact]
    public async Task ListAsync_ShouldApplyFromAndTo_Inclusively()
    {
        // Arrange: FR-017 — datas inclusivas.
        await SeedStandardCorpus();

        // Act
        var wholeMonth = await _sut.ListAsync(USER_A, new OrderListQuery
        {
            From = new DateOnly(2026, 10, 1),
            To = new DateOnly(2026, 10, 20)
        });
        var onlyFirstDay = await _sut.ListAsync(USER_A, new OrderListQuery
        {
            From = new DateOnly(2026, 10, 1),
            To = new DateOnly(2026, 10, 1)
        });
        var onlyLastDay = await _sut.ListAsync(USER_A, new OrderListQuery
        {
            From = new DateOnly(2026, 10, 20),
            To = new DateOnly(2026, 10, 20)
        });
        var outsideRange = await _sut.ListAsync(USER_A, new OrderListQuery
        {
            From = new DateOnly(2026, 10, 21),
            To = new DateOnly(2026, 10, 30)
        });

        // Assert: as bordas entram; nada fora do intervalo.
        Assert.Equal(3, wholeMonth.TotalCount);
        Assert.Single(onlyFirstDay.Items);
        Assert.Single(onlyLastDay.Items);
        Assert.Equal(0, outsideRange.TotalCount);
    }

    [Fact]
    public async Task ListAsync_ShouldFilterByKind()
    {
        // Arrange: FR-017.
        await SeedStandardCorpus();

        // Act
        var sales = await _sut.ListAsync(USER_A, new OrderListQuery { Kind = OrderKind.Sale });
        var returns = await _sut.ListAsync(USER_A, new OrderListQuery { Kind = OrderKind.Return });
        var cancellations = await _sut.ListAsync(
            USER_A, new OrderListQuery { Kind = OrderKind.Cancellation });

        // Assert
        AssertIds(sales.Items, 1);
        AssertIds(returns.Items, 2);
        AssertIds(cancellations.Items, 3);
    }

    [Fact]
    public async Task ListAsync_ShouldMatchChannelCaseInsensitively_OnEquality()
    {
        // Arrange: T074 — "linx" encontra "Linx" e vice-versa. O canal INTEIRO
        // continua casando depois de o filtro virar por trecho (T067): igualdade
        // é um caso particular de trecho, e essa compatibilidade é requisito de
        // contracts/api-consumption.md.
        await SeedStandardCorpus();

        // Act
        var lower = await _sut.ListAsync(USER_A, new OrderListQuery { Channel = "linx" });
        var upper = await _sut.ListAsync(USER_A, new OrderListQuery { Channel = "LINX" });

        // Assert: pedidos 1 ("Linx") e 3 (gravado como "linx"), data DESC.
        AssertIds(lower.Items, 3, 1);
        Assert.Equal(lower.TotalCount, upper.TotalCount);
    }

    [Fact]
    public async Task ListAsync_ShouldFilterChannelByChunk_CaseInsensitive()
    {
        // Arrange: FR-014 / T063 — a vendedora digita um TRECHO do canal ("lin"
        // encontra "Linx"). O isolamento por usuário vem primeiro (FR-021): o pedido 4,
        // do mesmo canal mas da outra vendedora, só aparece para a dona dele.
        await SeedStandardCorpus();

        // Act
        var chunk = await _sut.ListAsync(USER_A, new OrderListQuery { Channel = "inx" });
        var chunkUpper = await _sut.ListAsync(USER_A, new OrderListQuery { Channel = "INX" });
        var overlapping = await _sut.ListAsync(USER_A, new OrderListQuery { Channel = "lin" });
        var unknown = await _sut.ListAsync(USER_A, new OrderListQuery { Channel = "xyz" });
        var otherUser = await _sut.ListAsync(USER_B, new OrderListQuery { Channel = "lin" });

        // Assert: "inx" casa "Linx" (1) e "linx" (3), e nao "Online" (2). Data DESC.
        AssertIds(chunk.Items, 3, 1);
        Assert.Equal(chunk.TotalCount, chunkUpper.TotalCount);

        // "o-n-l-i-n-e" tambem contem o trecho "lin": consequencia correta de
        // filtrar por trecho — e o motivo de a tela mostrar o total de resultados.
        AssertIds(overlapping.Items, 3, 2, 1);
        Assert.Equal(0, unknown.TotalCount);

        // FR-021: o filtro por dono vem antes, entao o "Linx" da outra vendedora
        // aparece so para ela.
        AssertIds(otherUser.Items, 4);
    }

    [Fact]
    public async Task ListAsync_ShouldFilterByCustomerId()
    {
        // Arrange: FR-017.
        await SeedStandardCorpus();

        // Act
        var firstCustomer = await _sut.ListAsync(USER_A, new OrderListQuery { CustomerId = CUSTOMER_A1 });
        var secondCustomer = await _sut.ListAsync(USER_A, new OrderListQuery { CustomerId = CUSTOMER_A2 });

        // Assert
        AssertIds(firstCustomer.Items, 2, 1);
        AssertIds(secondCustomer.Items, 3);
    }

    [Fact]
    public async Task ListAsync_ShouldSearchOrderNumberOrCustomerName()
    {
        // Arrange: FR-017 — a busca atravessa pedido e cliente, sem diferenciar maiúsculas.
        await SeedStandardCorpus();

        // Act
        var byNumber = await _sut.ListAsync(USER_A, new OrderListQuery { Search = "4752" });
        var byCustomerName = await _sut.ListAsync(USER_A, new OrderListQuery { Search = "compradora" });
        var byCustomerNameUpper = await _sut.ListAsync(USER_A, new OrderListQuery { Search = "COMPRADORA" });
        var bySecondCustomer = await _sut.ListAsync(USER_A, new OrderListQuery { Search = "Bia Clie" });
        var nothing = await _sut.ListAsync(USER_A, new OrderListQuery { Search = "inexistente" });

        // Assert
        AssertIds(byNumber.Items, 2);

        // "Ana Compradora" é dona dos pedidos 1 e 2 (data DESC).
        AssertIds(byCustomerName.Items, 2, 1);
        Assert.Equal(byCustomerName.TotalCount, byCustomerNameUpper.TotalCount);
        AssertIds(bySecondCustomer.Items, 3);
        Assert.Equal(0, nothing.TotalCount);
    }

    [Fact]
    public async Task ListAsync_ShouldOrderByDateDescendingThenIdDescending()
    {
        // Arrange: FR-017 / research §9 — dois pedidos no MESMO dia provam o desempate por id.
        await SeedStandardCorpus();

        _context.Orders.Add(NewOrder(5, USER_A, CUSTOMER_A1, "00004755", new DateOnly(2026, 10, 20),
            OrderKind.Sale, "Linx", 20m));
        await _context.SaveChangesAsync();

        _context.OrderItems.Add(NewItem(105, 5, 1, "Saia Alfaiataria", 20m));
        await _context.SaveChangesAsync();

        // Act
        var (items, _) = await _sut.ListAsync(USER_A, new OrderListQuery());

        // Assert: 20/10 (id 5 antes do id 3), depois 10/10 e 01/10.
        AssertIds(items, 5, 3, 2, 1);
    }

    [Fact]
    public async Task ListAsync_ShouldPaginate_AndReturnEmptyPageWithCorrectTotalBeyondTheEnd()
    {
        // Arrange: FR-017 / FR-008.
        await SeedStandardCorpus();

        // Act
        var page1 = await _sut.ListAsync(USER_A, new OrderListQuery { Page = 1, PageSize = 2 });
        var page2 = await _sut.ListAsync(USER_A, new OrderListQuery { Page = 2, PageSize = 2 });
        var beyond = await _sut.ListAsync(USER_A, new OrderListQuery { Page = 9, PageSize = 2 });

        // Assert
        AssertIds(page1.Items, 3, 2);
        AssertIds(page2.Items, 1);
        Assert.Empty(beyond.Items);
        Assert.Equal(3, page1.TotalCount);
        Assert.Equal(3, page2.TotalCount);
        Assert.Equal(3, beyond.TotalCount);
    }

    [Fact]
    public async Task GetByIdAsync_ShouldIncludeCustomerAndItemsOrderedByLineNo()
    {
        // Arrange: FR-016 — itens inseridos fora de ordem (2, 3, 1).
        await SeedStandardCorpus();

        _context.Orders.Add(NewOrder(9, USER_A, CUSTOMER_A1, "00009999", new DateOnly(2026, 10, 5)));
        await _context.SaveChangesAsync();

        _context.OrderItems.AddRange(
            NewItem(902, 9, 2, "Blusa Lia", 189.90m),
            NewItem(903, 9, 3, "Cinto Eco", 49.90m),
            NewItem(901, 9, 1, "Calça Inez", 399.90m));
        await _context.SaveChangesAsync();

        _context.ChangeTracker.Clear();

        // Act
        var loaded = await _sut.GetByIdAsync(9, USER_A);

        // Assert
        Assert.NotNull(loaded);
        Assert.NotNull(loaded.Customer);
        Assert.Equal("Ana Compradora", loaded.Customer!.Name);
        Assert.Equal(3, loaded.Items.Count);
        Assert.Equal(new[] { 1, 2, 3 }, loaded.Items.Select(i => i.LineNo).ToArray());
        Assert.Equal("Calça Inez", loaded.Items[0].ProductName);
    }

    [Fact]
    public async Task GetByIdAsync_ShouldReturnNull_ForAnotherUser()
    {
        // Arrange: SC-002 — o pedido 1 existe, mas é de A.
        await SeedStandardCorpus();

        // Act
        var asOwner = await _sut.GetByIdAsync(1, USER_A);
        var asIntruder = await _sut.GetByIdAsync(1, USER_B);

        // Assert
        Assert.NotNull(asOwner);
        Assert.Null(asIntruder);
    }

    [Fact]
    public async Task ExistsAsync_ShouldConsiderUserNumberDateAndKind()
    {
        // Arrange: FR-014 — a unicidade é o quádruplo.
        await SeedStandardCorpus();

        // Act
        var sameEverything = await _sut.ExistsAsync(
            USER_A, "00004751", new DateOnly(2026, 10, 1), OrderKind.Sale);
        var otherKind = await _sut.ExistsAsync(
            USER_A, "00004751", new DateOnly(2026, 10, 1), OrderKind.Cancellation);
        var otherDate = await _sut.ExistsAsync(
            USER_A, "00004751", new DateOnly(2026, 10, 2), OrderKind.Sale);
        var sameNumberOtherUser = await _sut.ExistsAsync(
            USER_B, "00004751", new DateOnly(2026, 10, 24), OrderKind.Sale);
        var numberOfAUnderB = await _sut.ExistsAsync(
            USER_B, "00004752", new DateOnly(2026, 10, 10), OrderKind.Return);

        // Assert: mesmo número/data com OUTRO tipo não é duplicado (T074).
        Assert.True(sameEverything);
        Assert.False(otherKind);
        Assert.False(otherDate);
        Assert.True(sameNumberOtherUser);
        Assert.False(numberOfAUnderB);
    }

    [Fact]
    public async Task InsertAsync_ShouldPersistOrderAndItemsInOneSave()
    {
        // Arrange: FR-018 — pedido + itens numa única SaveChangesAsync.
        _context.Customers.Add(NewCustomer(CUSTOMER_A1, USER_A, "Ana Compradora"));
        await _context.SaveChangesAsync();

        var order = NewOrder(0, USER_A, CUSTOMER_A1, "00007000", new DateOnly(2026, 10, 3));
        order.Items.Add(NewItem(0, 0, 1, "Calça Inez", 399.90m));
        order.Items.Add(NewItem(0, 0, 2, "Blusa Lia", 189.90m));

        // Act
        var saved = await _sut.InsertAsync(order);

        // Assert
        Assert.True(saved.Id > 0);
        Assert.Equal(2, await _context.OrderItems.CountAsync(i => i.OrderId == saved.Id));
    }

    [Fact]
    public async Task UpdateAsync_ShouldPersistStatusAndTotal()
    {
        // Arrange: FR-019.
        await SeedStandardCorpus();
        _context.ChangeTracker.Clear();

        var loaded = await _sut.GetByIdAsync(1, USER_A);
        Assert.NotNull(loaded);

        loaded.UpdateStatusAndTotal(
            "Estornado", 80m, new DateTime(2026, 10, 25, 12, 0, 0, DateTimeKind.Unspecified));

        // Act
        var saved = await _sut.UpdateAsync(loaded);

        // Assert
        Assert.Equal("Estornado", saved.Status);
        Assert.Equal(80m, saved.Total);

        _context.ChangeTracker.Clear();
        var reread = await _sut.GetByIdAsync(1, USER_A);
        Assert.Equal("Estornado", reread!.Status);
        Assert.Equal(80m, reread.Total);
    }
}
