using Ambrosio.Domain.Exceptions;
using Ambrosio.Domain.Interfaces;
using Ambrosio.Domain.Models;
using Ambrosio.Domain.Services;
using Ambrosio.Domain.Validators;
using Ambrosio.DTO.Common;
using Ambrosio.DTO.CustomerPhotos;
using Ambrosio.DTO.Customers;
using Ambrosio.DTO.Enums;
using Ambrosio.DTO.Orders;
using Ambrosio.Infra.Interfaces.Repository;
using Ambrosio.Tests.TestHelpers;
using FluentValidation;
using Moq;

namespace Ambrosio.Tests.Domain.Services;

/// <summary>
/// T073 [US3]: regras de pedido (FR-012 a FR-019, FR-021 a FR-023) — cliente precisa pertencer ao
/// usuário, unicidade por (número, data, tipo), numeração sequencial de itens e mapeamento que só
/// expõe o CPF mascarado do cliente.
/// </summary>
public class OrderServiceTest
{
    private const long USER_ID = 7;
    private const long CUSTOMER_ID = 11;
    private const long ORDER_ID = 21;

    private static readonly DateTime NOW = new(2026, 10, 25, 12, 0, 0, DateTimeKind.Unspecified);

    private readonly Mock<IOrderRepository<Order>> _orders = new();
    private readonly Mock<ICustomerRepository<Customer>> _customers = new();
    private readonly Mock<ICustomerPhotoService> _photos = new();
    private readonly FakeClock _clock = new(NOW);
    private readonly OrderService _sut;

    public OrderServiceTest()
    {
        _customers.Setup(r => r.GetByIdAsync(CUSTOMER_ID, USER_ID)).ReturnsAsync(NewCustomer());

        _orders.Setup(r => r.ExistsAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<DateOnly>(),
                It.IsAny<OrderKind>()))
            .ReturnsAsync(false);
        _orders.Setup(r => r.InsertAsync(It.IsAny<Order>())).ReturnsAsync((Order o) => o);
        _orders.Setup(r => r.UpdateAsync(It.IsAny<Order>())).ReturnsAsync((Order o) => o);

        // Fotos (009): nenhuma cliente destes testes tem perfil nem álbum.
        _photos.Setup(p => p.GetProfilesAsync(It.IsAny<long>(), It.IsAny<IReadOnlyCollection<long>>(), It.IsAny<bool>()))
            .ReturnsAsync(new Dictionary<long, CustomerProfilePhotoInfo>());

        // Validators reais: as regras de formato (sinal do total, itens, data) são da própria
        // história; os mocks ficam nas fronteiras (repositórios, relógio).
        _sut = new OrderService(
            _orders.Object,
            _customers.Object,
            _photos.Object,
            new OrderInsertInfoValidator(_clock),
            new OrderUpdateInfoValidator(),
            new OrderListQueryValidator(),
            _clock);
    }

    private static Customer NewCustomer() => new()
    {
        Id = CUSTOMER_ID,
        UserId = USER_ID,
        Name = "Maria Silva",
        CpfHash = "hash",
        CpfEncrypted = "cifra",
        CpfMasked = "***.982.247-**",
        CreatedAt = NOW.AddDays(-5),
        UpdatedAt = NOW.AddDays(-5)
    };

    private static OrderItemInsertInfo Item(
        string name = "Calça Inez",
        decimal? price = 399.90m) => new()
    {
        ProductName = name,
        SkuRef = "42CA500UIE_12182",
        Size = "38",
        Price = price
    };

    private static OrderInsertInfo Insert(
        OrderKind kind = OrderKind.Sale,
        decimal total = 589.80m,
        List<OrderItemInsertInfo>? items = null) => new()
    {
        CustomerId = CUSTOMER_ID,
        OrderNumber = "00004751",
        OrderDate = new DateOnly(2026, 10, 24),
        Kind = kind,
        Channel = "Linx",
        Origin = OrderOrigin.Cota,
        Status = "Faturado",
        Total = total,
        Items = items ?? [Item(), Item("Blusa Lia", 189.90m)]
    };

    private static Order ExistingOrder(OrderKind kind = OrderKind.Sale, decimal total = 589.80m) => new()
    {
        Id = ORDER_ID,
        UserId = USER_ID,
        CustomerId = CUSTOMER_ID,
        OrderNumber = "00004751",
        OrderDate = new DateOnly(2026, 10, 24),
        Kind = kind,
        Channel = "Linx",
        Origin = OrderOrigin.Cota,
        Status = "Faturado",
        Total = total,
        CreatedAt = NOW.AddDays(-1),
        UpdatedAt = NOW.AddDays(-1),
        Customer = NewCustomer(),
        Items = [new OrderItem { Id = 1, LineNo = 1, ProductName = "Calça Inez", Price = 399.90m }]
    };

    [Fact]
    public async Task CreateAsync_ShouldThrowNotFound_AndNotInsert_WhenCustomerIsNotFromTheUser()
    {
        // Arrange: FR-023 / US4 cenário 3 — cliente inexistente OU de outro usuário (o
        // repositório devolve null nos dois casos).
        _customers.Setup(r => r.GetByIdAsync(CUSTOMER_ID, USER_ID)).ReturnsAsync((Customer?)null);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<NotFoundException>(
            () => _sut.CreateAsync(USER_ID, Insert()));

        Assert.Equal("Cliente não encontrado.", ex.Message);
        _orders.Verify(r => r.InsertAsync(It.IsAny<Order>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_ShouldThrowDuplicate_WhenNumberDateAndKindAlreadyExist()
    {
        // Arrange: FR-014.
        _orders.Setup(r => r.ExistsAsync(USER_ID, "00004751", new DateOnly(2026, 10, 24), OrderKind.Sale))
            .ReturnsAsync(true);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<DuplicateException>(
            () => _sut.CreateAsync(USER_ID, Insert()));

        Assert.Equal("Já existe um pedido com este número, data e tipo.", ex.Message);
        _orders.Verify(r => r.InsertAsync(It.IsAny<Order>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_ShouldPersistUserFromToken_CustomerAndNumberedItems()
    {
        // Act: FR-012, FR-018, FR-021.
        await _sut.CreateAsync(USER_ID, Insert());

        // Assert
        _orders.Verify(r => r.InsertAsync(It.Is<Order>(o =>
            o.UserId == USER_ID
            && o.CustomerId == CUSTOMER_ID
            && o.OrderNumber == "00004751"
            && o.OrderDate == new DateOnly(2026, 10, 24)
            && o.Kind == OrderKind.Sale
            && o.Channel == "Linx"
            && o.Origin == OrderOrigin.Cota
            && o.Status == "Faturado"
            && o.Total == 589.80m
            && o.CreatedAt == NOW
            && o.UpdatedAt == NOW
            && o.Items.Count == 2
            && o.Items[0].LineNo == 1
            && o.Items[1].LineNo == 2
            && o.Items.All(i => i.OrderId == 0 || i.OrderId == o.Id))), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_ShouldTrimTextFields()
    {
        // Arrange: o +Vendas traz textos com espaços; a coluna tem limite de tamanho.
        var info = Insert();
        info.OrderNumber = "  00004751  ";
        info.Channel = "  Linx  ";
        info.Status = "  Faturado  ";
        info.ExternalId = "  ext-1  ";
        info.Items = [Item(name: "  Calça Inez  ")];
        info.Items[0].SkuRef = "  42CA500UIE_12182  ";
        info.Items[0].Size = "  38  ";

        // Act
        await _sut.CreateAsync(USER_ID, info);

        // Assert
        _orders.Verify(r => r.InsertAsync(It.Is<Order>(o =>
            o.OrderNumber == "00004751"
            && o.Channel == "Linx"
            && o.Status == "Faturado"
            && o.ExternalId == "ext-1"
            && o.Items[0].ProductName == "Calça Inez"
            && o.Items[0].SkuRef == "42CA500UIE_12182"
            && o.Items[0].Size == "38")), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_ShouldKeepCancellationDatesAndNegativeTotal()
    {
        // Arrange: SC-006 — cancelamento com total negativo e as datas, sem perda de informação.
        var info = Insert(OrderKind.Cancellation, -589.80m, [Item(price: -399.90m)]);
        info.OrderDate = new DateOnly(2026, 10, 25);
        info.Status = "Cancelado";
        info.OriginalSaleOn = new DateOnly(2026, 10, 24);
        info.CancelledOn = new DateOnly(2026, 10, 25);

        // Act
        var result = await _sut.CreateAsync(USER_ID, info);

        // Assert
        _orders.Verify(r => r.InsertAsync(It.Is<Order>(o =>
            o.Kind == OrderKind.Cancellation
            && o.Total == -589.80m
            && o.OriginalSaleOn == new DateOnly(2026, 10, 24)
            && o.CancelledOn == new DateOnly(2026, 10, 25))), Times.Once);

        Assert.Equal(-589.80m, result.Total);
        Assert.Equal(new DateOnly(2026, 10, 24), result.OriginalSaleOn);
        Assert.Equal(new DateOnly(2026, 10, 25), result.CancelledOn);
    }

    [Theory]
    [InlineData(OrderKind.Sale, -100.00)]
    [InlineData(OrderKind.Sale, 0.00)]
    [InlineData(OrderKind.Cancellation, 100.00)]
    public async Task CreateAsync_ShouldThrowValidation_WhenTotalSignConflictsWithKind(
        OrderKind kind, decimal total)
    {
        // Act & Assert: FR-013, antes de tocar o banco.
        await Assert.ThrowsAsync<ValidationException>(
            () => _sut.CreateAsync(USER_ID, Insert(kind, total)));

        _orders.Verify(r => r.InsertAsync(It.IsAny<Order>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_ShouldThrowValidation_WhenThereAreNoItems()
    {
        // FR-015.
        await Assert.ThrowsAsync<ValidationException>(
            () => _sut.CreateAsync(USER_ID, Insert(items: [])));

        _orders.Verify(r => r.InsertAsync(It.IsAny<Order>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_ShouldThrowValidation_WhenOrderDateIsFuture()
    {
        // FR-012 com o relógio congelado em 2026-10-25.
        var info = Insert();
        info.OrderDate = new DateOnly(2026, 10, 26);

        await Assert.ThrowsAsync<ValidationException>(() => _sut.CreateAsync(USER_ID, info));
    }

    [Fact]
    public async Task GetByIdAsync_ShouldReturnCustomerMaskedCpfAndItems()
    {
        // Arrange: FR-016.
        _orders.Setup(r => r.GetByIdAsync(ORDER_ID, USER_ID)).ReturnsAsync(ExistingOrder());

        // Act
        var result = await _sut.GetByIdAsync(USER_ID, ORDER_ID);

        // Assert
        Assert.Equal(ORDER_ID, result.Id);
        Assert.Equal(CUSTOMER_ID, result.CustomerId);
        Assert.Equal("Maria Silva", result.CustomerName);
        Assert.Equal("***.982.247-**", result.Customer.CpfMasked);
        Assert.Equal(1, result.Items.Count);
        Assert.Equal(1, result.Items[0].LineNo);
        Assert.Equal("Calça Inez", result.Items[0].ProductName);
        Assert.Equal(1, result.ItemsCount);
        Assert.Equal(OrderKind.Sale, result.Kind);
    }

    [Theory]
    [InlineData(ORDER_ID, USER_ID)]
    public async Task GetByIdAsync_ShouldThrowNotFound_WhenRepositoryReturnsNull(long orderId, long userId)
    {
        // Arrange: null significa "não existe" ou "é de outro usuário" (FR-022) — mesma resposta.
        _orders.Setup(r => r.GetByIdAsync(orderId, userId)).ReturnsAsync((Order?)null);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetByIdAsync(userId, orderId));

        Assert.Equal("Pedido não encontrado.", ex.Message);
    }

    [Fact]
    public async Task ListAsync_ShouldMapListingWithoutFullCpfAndWithItemsCount()
    {
        // Arrange: SC-005 — nem na listagem de pedidos o CPF completo aparece.
        _orders.Setup(r => r.ListAsync(It.IsAny<long>(), It.IsAny<OrderListQuery>()))
            .ReturnsAsync((new List<Order> { ExistingOrder() }, 1));

        // Act
        PagedResult<OrderInfo> result = await _sut.ListAsync(USER_ID,
            new OrderListQuery { Page = 1, PageSize = 20 });

        // Assert
        var item = Assert.Single(result.Items);
        Assert.Equal(1, item.ItemsCount);
        Assert.Equal("Maria Silva", item.CustomerName);
        Assert.Equal(1, result.TotalCount);

        var orderInfoProperties = typeof(OrderInfo).GetProperties().Select(p => p.Name);
        Assert.DoesNotContain(orderInfoProperties, n => n.Contains("Cpf", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ListAsync_ShouldPassQueryThroughAndValidatePaging()
    {
        // Arrange: FR-017.
        _orders.Setup(r => r.ListAsync(It.IsAny<long>(), It.IsAny<OrderListQuery>()))
            .ReturnsAsync((new List<Order>(), 0));

        var query = new OrderListQuery
        {
            From = new DateOnly(2026, 10, 1),
            To = new DateOnly(2026, 10, 31),
            Kind = OrderKind.Sale,
            Channel = "linx",
            CustomerId = CUSTOMER_ID,
            Search = "00004751",
            Page = 2,
            PageSize = 10
        };

        // Act
        await _sut.ListAsync(USER_ID, query);

        // Assert: o objeto de filtro chega intacto ao repositório.
        _orders.Verify(r => r.ListAsync(USER_ID, query), Times.Once);
    }

    [Fact]
    public async Task ListAsync_ShouldThrowValidation_WhenIntervalIsInverted()
    {
        await Assert.ThrowsAsync<ValidationException>(() => _sut.ListAsync(USER_ID,
            new OrderListQuery { From = new DateOnly(2026, 10, 31), To = new DateOnly(2026, 10, 1) }));

        _orders.Verify(r => r.ListAsync(It.IsAny<long>(), It.IsAny<OrderListQuery>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_ShouldChangeStatusAndTotal_WithTheStoredKindSign()
    {
        // Arrange: FR-019.
        _orders.Setup(r => r.GetByIdAsync(ORDER_ID, USER_ID)).ReturnsAsync(ExistingOrder());

        // Act
        var result = await _sut.UpdateAsync(USER_ID, ORDER_ID,
            new OrderUpdateInfo { Status = "Faturado", Total = 579.80m });

        // Assert
        _orders.Verify(r => r.UpdateAsync(It.Is<Order>(o =>
            o.Status == "Faturado"
            && o.Total == 579.80m
            && o.UpdatedAt == NOW)), Times.Once);
        Assert.Equal(579.80m, result.Total);
    }

    [Fact]
    public async Task UpdateAsync_ShouldThrowValidationOnTotal_WhenSignConflictsWithStoredKind()
    {
        // Arrange: pedido gravado como SALE; a atualização tenta um total negativo.
        _orders.Setup(r => r.GetByIdAsync(ORDER_ID, USER_ID)).ReturnsAsync(ExistingOrder(OrderKind.Sale));

        // Act & Assert: T073 — ValidationException no campo `total`.
        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => _sut.UpdateAsync(USER_ID, ORDER_ID,
                new OrderUpdateInfo { Status = "Cancelado", Total = -589.80m }));

        Assert.Equal("total", ex.Errors.Single().PropertyName);
        Assert.Equal("O total de uma venda deve ser positivo.", ex.Errors.Single().ErrorMessage);
        _orders.Verify(r => r.UpdateAsync(It.IsAny<Order>()), Times.Never);
    }

    [Theory]
    [InlineData(OrderKind.Return, 100.00)]
    [InlineData(OrderKind.Cancellation, 100.00)]
    public async Task UpdateAsync_ShouldRejectPositiveTotal_WhenStoredKindIsReturnOrCancellation(
        OrderKind storedKind, decimal total)
    {
        // Arrange: o sinal é checado contra o kind GRAVADO, não contra o enviado (kind é imutável).
        _orders.Setup(r => r.GetByIdAsync(ORDER_ID, USER_ID))
            .ReturnsAsync(ExistingOrder(storedKind, -100.00m));

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => _sut.UpdateAsync(USER_ID, ORDER_ID, new OrderUpdateInfo { Total = total }));

        Assert.Equal("total", ex.Errors.Single().PropertyName);
    }

    [Fact]
    public async Task UpdateAsync_ShouldThrowNotFound_WhenOrderIsFromAnotherUser()
    {
        // Arrange: FR-022.
        _orders.Setup(r => r.GetByIdAsync(ORDER_ID, USER_ID)).ReturnsAsync((Order?)null);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<NotFoundException>(
            () => _sut.UpdateAsync(USER_ID, ORDER_ID, new OrderUpdateInfo { Total = 10m }));

        Assert.Equal("Pedido não encontrado.", ex.Message);
        _orders.Verify(r => r.UpdateAsync(It.IsAny<Order>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_ShouldThrowValidation_WhenTotalIsZero()
    {
        _orders.Setup(r => r.GetByIdAsync(ORDER_ID, USER_ID)).ReturnsAsync(ExistingOrder());

        await Assert.ThrowsAsync<ValidationException>(
            () => _sut.UpdateAsync(USER_ID, ORDER_ID, new OrderUpdateInfo { Total = 0m }));

        _orders.Verify(r => r.UpdateAsync(It.IsAny<Order>()), Times.Never);
    }
}
