using Ambrosio.Domain.Models;
using Ambrosio.DTO.Enums;
using Ambrosio.Infra.Context;
using Ambrosio.Infra.Repository;
using Microsoft.EntityFrameworkCore;

namespace Ambrosio.Tests.Infra.Repository;

/// <summary>
/// T055 [US2]: isolamento, busca, ordenação e paginação de clientes, e a agregação do resumo de
/// compras (FR-008, FR-009, FR-021, FR-022) sobre <c>UseInMemoryDatabase</c> (research §17).
/// </summary>
public class CustomerRepositoryTest : IDisposable
{
    private const long USER_A = 1;
    private const long USER_B = 2;

    private static readonly DateOnly Today = new(2026, 10, 25);

    private readonly AmbrosioContext _context;
    private readonly CustomerRepository _sut;

    public CustomerRepositoryTest()
    {
        // Nome único por instância: os testes xUnit rodam em paralelo e compartilhariam o estado
        // InMemory se a chave fosse fixa.
        _context = new AmbrosioContext(new DbContextOptionsBuilder<AmbrosioContext>()
            .UseInMemoryDatabase($"customers-{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics
                .InMemoryEventId.TransactionIgnoredWarning))
            .Options);

        _sut = new CustomerRepository(_context);
    }

    public void Dispose() => _context.Dispose();

    private static Customer NewCustomer(
        long id,
        long userId,
        string name,
        string cpfHash = "hash") => new()
    {
        Id = id,
        UserId = userId,
        Name = name,
        CpfHash = cpfHash,
        CpfEncrypted = "cifra",
        CpfMasked = "***.000.000-**",
        CreatedAt = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Unspecified),
        UpdatedAt = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Unspecified)
    };

    private static Order NewOrder(
        long id,
        long userId,
        long customerId,
        decimal total,
        OrderKind kind,
        DateOnly orderDate) => new()
    {
        Id = id,
        UserId = userId,
        CustomerId = customerId,
        OrderNumber = $"pedido-{id}",
        OrderDate = orderDate,
        Kind = kind,
        Total = total,
        CreatedAt = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Unspecified),
        UpdatedAt = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Unspecified)
    };

    [Fact]
    public async Task ListAsync_ShouldReturnOnlyCustomersOfTheUser()
    {
        // Arrange: FR-021 — nada de outro usuário aparece, nem com busca vazia.
        _context.Customers.AddRange(
            NewCustomer(1, USER_A, "Ana Tricotom"),
            NewCustomer(2, USER_B, "Bia Ramalho"),
            NewCustomer(3, USER_A, "Carla Vieira"));
        await _context.SaveChangesAsync();

        // Act
        var (items, totalCount) = await _sut.ListAsync(USER_A, search: null, page: 1, pageSize: 20);

        // Assert
        Assert.Equal(2, totalCount);
        Assert.All(items, c => Assert.Equal(USER_A, c.UserId));
        Assert.DoesNotContain(items, c => c.Name == "Bia Ramalho");
    }

    [Fact]
    public async Task ListAsync_ShouldFilterByNameCaseInsensitively()
    {
        // Arrange: a busca é por trecho e não diferencia maiúsculas (FR-008, research §9).
        _context.Customers.AddRange(
            NewCustomer(1, USER_A, "Maria Silva"),
            NewCustomer(2, USER_A, "Marta Souza"),
            NewCustomer(3, USER_A, "Joana Lima"));
        await _context.SaveChangesAsync();

        // Act: três grafias do mesmo trecho.
        var lower = await _sut.ListAsync(USER_A, "mar", 1, 20);
        var upper = await _sut.ListAsync(USER_A, "MAR", 1, 20);
        var mixed = await _sut.ListAsync(USER_A, "Mar", 1, 20);

        // Assert: "Maria" e "Marta" contêm "mar"; "Joana" não.
        Assert.Equal(2, lower.TotalCount);
        Assert.Equal(2, upper.TotalCount);
        Assert.Equal(2, mixed.TotalCount);
    }

    [Fact]
    public async Task ListAsync_ShouldOrderByAscendingName()
    {
        // Arrange: ordenação por nome (research §9); o desempate por id mantém a ordem estável.
        _context.Customers.AddRange(
            NewCustomer(3, USER_A, "Célia"),
            NewCustomer(1, USER_A, "Ana"),
            NewCustomer(2, USER_A, "Bia"));
        await _context.SaveChangesAsync();

        // Act
        var (items, _) = await _sut.ListAsync(USER_A, null, 1, 20);

        // Assert
        Assert.Equal(new[] { "Ana", "Bia", "Célia" }, items.Select(c => c.Name));
    }

    [Fact]
    public async Task ListAsync_ShouldPageTheResults()
    {
        // Arrange: 5 clientes, páginas de 2.
        for (var i = 1; i <= 5; i++)
        {
            _context.Customers.Add(NewCustomer(i, USER_A, $"Cliente {i:00}"));
        }
        await _context.SaveChangesAsync();

        // Act
        var (page1, total1) = await _sut.ListAsync(USER_A, null, 1, 2);
        var (page2, total2) = await _sut.ListAsync(USER_A, null, 2, 2);
        var (page3, total3) = await _sut.ListAsync(USER_A, null, 3, 2);

        // Assert
        Assert.Equal(new[] { "Cliente 01", "Cliente 02" }, page1.Select(c => c.Name));
        Assert.Equal(new[] { "Cliente 03", "Cliente 04" }, page2.Select(c => c.Name));
        Assert.Single(page3);
        Assert.Equal("Cliente 05", page3[0].Name);

        Assert.Equal(5, total1);
        Assert.Equal(5, total2);
        Assert.Equal(5, total3);
    }

    [Fact]
    public async Task ListAsync_ShouldReturnEmptyPageBeyondTheEnd_WithTheRightTotalCount()
    {
        // Arrange: FR-008 — página além do fim devolve items: [] e totalCount correto.
        _context.Customers.AddRange(
            NewCustomer(1, USER_A, "Ana"),
            NewCustomer(2, USER_A, "Bia"));
        await _context.SaveChangesAsync();

        // Act
        var (items, totalCount) = await _sut.ListAsync(USER_A, null, page: 9, pageSize: 20);

        // Assert
        Assert.Empty(items);
        Assert.Equal(2, totalCount);
    }

    [Fact]
    public async Task GetByIdAsync_ShouldReturnNull_WhenCustomerBelongsToAnotherUser()
    {
        // Arrange: FR-022/SC-002 — o recurso existe, mas não é do usuário.
        _context.Customers.Add(NewCustomer(11, USER_A, "Ana"));
        await _context.SaveChangesAsync();

        // Act
        var asOwner = await _sut.GetByIdAsync(11, USER_A);
        var asIntruder = await _sut.GetByIdAsync(11, USER_B);

        // Assert: o repositório devolve null nos dois casos de não-propriedade; o serviço decide
        // a mensagem 404 (idêntica à de inexistente).
        Assert.NotNull(asOwner);
        Assert.Null(asIntruder);
    }

    [Fact]
    public async Task GetByIdAsync_ShouldReturnNull_WhenIdDoesNotExist()
    {
        _context.Customers.Add(NewCustomer(11, USER_A, "Ana"));
        await _context.SaveChangesAsync();

        Assert.Null(await _sut.GetByIdAsync(999, USER_A));
    }

    [Fact]
    public async Task ExistsByCpfHashAsync_ShouldConsiderOnlyTheOwnersRecords()
    {
        // Arrange: FR-010/US4 — o mesmo CPF (mesmo hash) pode pertencer a outro usuário.
        _context.Customers.Add(NewCustomer(11, USER_A, "Ana", cpfHash: "cpf-de-ana"));
        await _context.SaveChangesAsync();

        // Act
        var forOwner = await _sut.ExistsByCpfHashAsync(USER_A, "cpf-de-ana");
        var forIntruder = await _sut.ExistsByCpfHashAsync(USER_B, "cpf-de-ana");
        var forOtherHash = await _sut.ExistsByCpfHashAsync(USER_A, "outro");

        // Assert
        Assert.True(forOwner);
        Assert.False(forIntruder);
        Assert.False(forOtherHash);
    }

    [Fact]
    public async Task GetSummaryAsync_ShouldReturnZeros_WhenCustomerHasNoOrders()
    {
        // Arrange: FR-009 com cliente sem pedidos (Cenário 2 do quickstart).
        _context.Customers.Add(NewCustomer(11, USER_A, "Ana"));
        await _context.SaveChangesAsync();

        // Act
        var summary = await _sut.GetSummaryAsync(11, USER_A, Today);

        // Assert
        Assert.Equal(0, summary.Purchases);
        Assert.Equal(0, summary.NetTotal);
        Assert.Null(summary.LastPurchase);
        Assert.Null(summary.DaysSinceLastPurchase);
    }

    [Fact]
    public async Task GetSummaryAsync_ShouldAggregateSalesAndReturns_IgnoringOtherUsers()
    {
        // Arrange: cenários da tarefa — duas vendas, uma devolução e um pedido de outro usuário
        // sobre um cliente com o mesmo id.
        _context.Customers.AddRange(
            NewCustomer(11, USER_A, "Ana"),
            NewCustomer(12, USER_B, "Bia"));
        await _context.SaveChangesAsync();

        _context.Orders.AddRange(
            NewOrder(1, USER_A, 11, 100.00m, OrderKind.Sale, new DateOnly(2026, 10, 1)),
            NewOrder(2, USER_A, 11, 50.00m, OrderKind.Sale, new DateOnly(2026, 10, 20)),
            NewOrder(3, USER_A, 11, -30.00m, OrderKind.Return, new DateOnly(2026, 10, 22)),
            NewOrder(4, USER_B, 12, 9999.00m, OrderKind.Sale, new DateOnly(2026, 10, 24)));
        await _context.SaveChangesAsync();

        // Act
        var summary = await _sut.GetSummaryAsync(11, USER_A, Today);

        // Assert: purchases conta apenas vendas; netTotal soma tudo (research §10).
        Assert.Equal(2, summary.Purchases);
        Assert.Equal(120.00m, summary.NetTotal);
        Assert.Equal(new DateOnly(2026, 10, 20), summary.LastPurchase);
        Assert.Equal((Today.DayNumber - new DateOnly(2026, 10, 20).DayNumber), summary.DaysSinceLastPurchase);
        Assert.Equal(5, summary.DaysSinceLastPurchase);
    }

    [Fact]
    public async Task GetSummaryAsync_ShouldIgnoreReturnsOfTheLastSaleDate()
    {
        // Arrange: a devolução é mais recente, mas lastPurchase olha só vendas (data-model).
        _context.Customers.Add(NewCustomer(11, USER_A, "Ana"));
        await _context.SaveChangesAsync();

        _context.Orders.AddRange(
            NewOrder(1, USER_A, 11, 100.00m, OrderKind.Sale, new DateOnly(2026, 10, 1)),
            NewOrder(2, USER_A, 11, -20.00m, OrderKind.Return, new DateOnly(2026, 10, 15)),
            NewOrder(3, USER_A, 11, -15.00m, OrderKind.Cancellation, new DateOnly(2026, 10, 18)));
        await _context.SaveChangesAsync();

        // Act
        var summary = await _sut.GetSummaryAsync(11, USER_A, Today);

        // Assert
        Assert.Equal(1, summary.Purchases);
        Assert.Equal(65.00m, summary.NetTotal);
        Assert.Equal(new DateOnly(2026, 10, 1), summary.LastPurchase);
    }

    [Fact]
    public async Task GetSummaryAsync_ShouldReturnNull_WhenTheUserHasNoSaleAtAll()
    {
        // Arrange: só devolução, sem venda: purchases = 0, netTotal negativo, lastPurchase null.
        _context.Customers.Add(NewCustomer(11, USER_A, "Ana"));
        await _context.SaveChangesAsync();

        _context.Orders.Add(
            NewOrder(1, USER_A, 11, -25.00m, OrderKind.Return, new DateOnly(2026, 10, 10)));
        await _context.SaveChangesAsync();

        // Act
        var summary = await _sut.GetSummaryAsync(11, USER_A, Today);

        // Assert
        Assert.Equal(0, summary.Purchases);
        Assert.Equal(-25.00m, summary.NetTotal);
        Assert.Null(summary.LastPurchase);
        Assert.Null(summary.DaysSinceLastPurchase);
    }

    [Fact]
    public async Task GetSummaryAsync_ShouldReturnZeros_WhenCustomerIdBelongsToAnotherUser()
    {
        // Arrange: SC-002 — B pedindo o resumo do cliente de A não vê nada.
        _context.Customers.Add(NewCustomer(11, USER_A, "Ana"));
        await _context.SaveChangesAsync();
        _context.Orders.Add(NewOrder(1, USER_A, 11, 100.00m, OrderKind.Sale, new DateOnly(2026, 10, 1)));
        await _context.SaveChangesAsync();

        // Act
        var summary = await _sut.GetSummaryAsync(11, USER_B, Today);

        // Assert
        Assert.Equal(0, summary.Purchases);
        Assert.Equal(0, summary.NetTotal);
        Assert.Null(summary.LastPurchase);
    }

    [Fact]
    public async Task InsertAsync_ShouldPersistTheCustomerAndReturnIt()
    {
        // Arrange
        var customer = NewCustomer(0, USER_A, "Novo Cliente");

        // Act
        var saved = await _sut.InsertAsync(customer);

        // Assert
        Assert.Equal("Novo Cliente", saved.Name);
        Assert.Equal(1, await _context.Customers.CountAsync());
    }

    [Fact]
    public async Task UpdateAsync_ShouldPersistTheNewName()
    {
        // Arrange
        _context.Customers.Add(NewCustomer(11, USER_A, "Nome Antigo"));
        await _context.SaveChangesAsync();

        var loaded = await _context.Customers.FirstAsync(c => c.Id == 11);
        loaded.Name = "Nome Novo";
        loaded.UpdatedAt = new DateTime(2026, 10, 25, 12, 0, 0, DateTimeKind.Unspecified);

        // Act
        var updated = await _sut.UpdateAsync(loaded);

        // Assert
        Assert.Equal("Nome Novo", updated.Name);
        _context.ChangeTracker.Clear();
        Assert.Equal("Nome Novo", (await _context.Customers.FirstAsync(c => c.Id == 11)).Name);
    }
}
