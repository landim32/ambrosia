using System.Text.Json;
using Ambrosio.ApiTests.Fixtures;
using Ambrosio.ApiTests.Helpers;
using Ambrosio.DTO.Common;
using Ambrosio.DTO.Customers;
using Ambrosio.DTO.Enums;
using Ambrosio.DTO.Orders;
using FluentAssertions;

namespace Ambrosio.ApiTests.Controllers;

/// <summary>
/// T076 [US3]: <c>/orders</c> ponta a ponta (quickstart, Cenário 3) — venda com itens numerados,
/// cancelamento com total negativo e datas (SC-006), recusas de 400 por sinal/itens/data futura,
/// duplicidade 409, listagem filtrada e ordenada, detalhe com cliente e itens, atualização de
/// status/total e o reflexo no resumo do cliente.
/// </summary>
[Collection(ApiTestCollection.API_TESTS)]
public class OrdersControllerTests
{
    private const string ORDERS = "/orders";
    private const string CUSTOMERS = "/customers";

    private readonly ApiTestFixture _fixture;

    public OrdersControllerTests(ApiTestFixture fixture) => _fixture = fixture;

    private string Token => _fixture.TokenA;

    private async Task<CustomerDetailInfo> CreateCustomerAsync()
    {
        var response = await _fixture.PostAsync(CUSTOMERS, Token, TestDataHelper.NewCustomer());

        response.StatusCode.Should().Be(201, $"o cliente deveria ser criado: {response}");

        return response.As<CustomerDetailInfo>();
    }

    private async Task<OrderDetailInfo> CreateOrderAsync(OrderInsertInfo payload)
    {
        var response = await _fixture.PostAsync(ORDERS, Token, payload);

        response.StatusCode.Should().Be(201, $"o pedido deveria ser criado: {response}");

        return response.As<OrderDetailInfo>();
    }

    /// <summary>Cliente + venda do quickstart, para os testes que só precisam do cenário base.</summary>
    private async Task<(CustomerDetailInfo Customer, OrderDetailInfo Sale, OrderInsertInfo Payload)>
        NewSaleScenarioAsync()
    {
        var customer = await CreateCustomerAsync();
        var payload = TestDataHelper.NewSaleOrder(customer.Id);
        var sale = await CreateOrderAsync(payload);

        return (customer, sale, payload);
    }

    [Fact]
    public async Task Create_Sale_ShouldReturn201WithNumberedItems()
    {
        // Arrange: FR-012, FR-018.
        var customer = await CreateCustomerAsync();
        var payload = TestDataHelper.NewSaleOrder(customer.Id);

        // Act
        var response = await _fixture.PostAsync(ORDERS, Token, payload);

        // Assert: 201 + Location /orders/{id}.
        response.StatusCode.Should().Be(201);

        var created = response.As<OrderDetailInfo>();
        response.Location.Should().EndWith($"/{created.Id}");

        created.Id.Should().BeGreaterThan(0);
        created.CustomerId.Should().Be(customer.Id);
        created.OrderNumber.Should().Be(payload.OrderNumber);
        created.Kind.Should().Be(OrderKind.Sale);
        created.Total.Should().Be(TestDataHelper.SALE_TOTAL);
        created.ItemsCount.Should().Be(2);

        // LineNo atribuído pelo modelo, na ordem enviada.
        created.Items.Select(i => i.LineNo).Should().Equal(1, 2);
        created.Items[0].ProductName.Should().Be("Calça Inez");
        created.Items[1].SkuRef.Should().Be("41BL200AAA_00001");
    }

    [Fact]
    public async Task Create_Cancellation_NegativeTotal_ShouldReturn201KeepingDates()
    {
        // Arrange: SC-006 — cancelamento do mesmo número, tipo diferente, total negativo.
        var (customer, _, payload) = await NewSaleScenarioAsync();
        var cancellation = TestDataHelper.NewCancellationOrder(customer.Id, payload.OrderNumber);

        // Act
        var response = await _fixture.PostAsync(ORDERS, Token, cancellation);

        // Assert: nada de informação perdida (datas, status, preço negativo do item).
        response.StatusCode.Should().Be(201);

        var created = response.As<OrderDetailInfo>();
        created.Kind.Should().Be(OrderKind.Cancellation);
        created.Total.Should().Be(-TestDataHelper.SALE_TOTAL);
        created.Status.Should().Be("Cancelado");
        created.OriginalSaleOn.Should().Be(TestDataHelper.SaleDate);
        created.CancelledOn.Should().NotBeNull();
        created.Items.Should().ContainSingle();
        created.Items[0].Price.Should().Be(-399.90m);
    }

    [Fact]
    public async Task Create_SaleNegativeTotal_ShouldReturn400WithCampoTotal()
    {
        // Arrange: FR-013.
        var customer = await CreateCustomerAsync();
        var payload = TestDataHelper.NewSaleOrder(customer.Id);
        payload.Total = -589.80m;

        // Act
        var response = await _fixture.PostAsync(ORDERS, Token, payload);

        // Assert: FR-024 — o campo apontado é "total".
        response.StatusCode.Should().Be(400);

        using var document = JsonDocument.Parse(response.Body);
        var totalError = document.RootElement.GetProperty("erros").EnumerateArray()
            .Single(e => e.GetProperty("campo").GetString() == "total");

        totalError.GetProperty("mensagem").GetString()
            .Should().Be("O total de uma venda deve ser positivo.");
    }

    [Fact]
    public async Task Create_NoItems_ShouldReturn400WithCampoItems()
    {
        // Arrange: FR-015.
        var customer = await CreateCustomerAsync();
        var payload = TestDataHelper.NewSaleOrder(customer.Id);
        payload.Items = [];

        // Act
        var response = await _fixture.PostAsync(ORDERS, Token, payload);

        // Assert
        response.StatusCode.Should().Be(400);
        response.Body.Should().Contain("\"items\"");
        response.Body.Should().Contain("ao menos um item");
    }

    [Fact]
    public async Task Create_FutureDate_ShouldReturn400WithCampoOrderDate()
    {
        // Arrange: FR-012.
        var customer = await CreateCustomerAsync();
        var payload = TestDataHelper.NewSaleOrder(customer.Id);
        payload.OrderDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);

        // Act
        var response = await _fixture.PostAsync(ORDERS, Token, payload);

        // Assert
        response.StatusCode.Should().Be(400);
        response.Body.Should().Contain("\"orderDate\"");
        response.Body.Should().Contain("não pode ser futura");
    }

    [Fact]
    public async Task Create_Duplicate_ShouldReturn409()
    {
        // Arrange: FR-014 — mesmo número, data e tipo.
        var (customer, _, payload) = await NewSaleScenarioAsync();

        // Act: repete exatamente o mesmo corpo.
        var response = await _fixture.PostAsync(ORDERS, Token, payload);

        // Assert
        response.StatusCode.Should().Be(409);

        using var document = JsonDocument.Parse(response.Body);
        document.RootElement.GetProperty("mensagem").GetString()
            .Should().Be("Já existe um pedido com este número, data e tipo.");
    }

    [Fact]
    public async Task Create_UnknownChannel_ShouldReturn201()
    {
        // Arrange: Edge Cases da spec — canais fora da lista conhecida são aceitos (SC-006),
        // porque channel é texto livre (plan.md, Complexity Tracking).
        var customer = await CreateCustomerAsync();
        var payload = TestDataHelper.NewSaleOrder(customer.Id);
        payload.Channel = "Outlet";

        // Act
        var response = await _fixture.PostAsync(ORDERS, Token, payload);

        // Assert
        response.StatusCode.Should().Be(201);
        response.As<OrderDetailInfo>().Channel.Should().Be("Outlet");
    }

    [Fact]
    public async Task List_WithFilters_ShouldReturnMatchesOrderedByDateDesc()
    {
        // Arrange: FR-017 — cliente com duas vendas em dias diferentes + um cancelamento, e um
        // cliente sem pedidos para provar o filtro por customerId.
        var tag = Guid.NewGuid().ToString("N")[..8];
        var customer = await CreateCustomerAsync();

        var early = TestDataHelper.NewSaleOrder(customer.Id, $"early-{tag}");
        early.OrderDate = TestDataHelper.SaleDate;
        await CreateOrderAsync(early);

        var late = TestDataHelper.NewSaleOrder(customer.Id, $"late-{tag}");
        late.OrderDate = DateOnly.FromDateTime(DateTime.UtcNow);
        await CreateOrderAsync(late);

        var cancellation = TestDataHelper.NewCancellationOrder(customer.Id, $"early-{tag}");
        cancellation.OrderDate = DateOnly.FromDateTime(DateTime.UtcNow);
        await CreateOrderAsync(cancellation);

        // Act 1: intervalo + tipo + canal + cliente.
        var filtered = await _fixture.GetAsync(
            $"{ORDERS}?from={TestDataHelper.SaleDate:yyyy-MM-dd}" +
            $"&to={DateOnly.FromDateTime(DateTime.UtcNow):yyyy-MM-dd}" +
            $"&kind=sale&channel=linx&customerId={customer.Id}&page=1&pageSize=20",
            Token);

        // Assert: só as duas vendas do cliente, data decrescente.
        filtered.StatusCode.Should().Be(200);

        var page = filtered.As<PagedResult<OrderInfo>>();
        page.Items.Should().HaveCount(2);
        page.TotalCount.Should().Be(2);
        page.Items.Should().OnlyContain(o => o.Kind == OrderKind.Sale);
        page.Items.Should().OnlyContain(o => o.CustomerId == customer.Id);
        page.Items[0].OrderDate.ToDateTime(TimeOnly.MinValue).Should().BeAfter(page.Items[1].OrderDate.ToDateTime(TimeOnly.MinValue), "ordenacao por data decrescente");
        page.Items[0].CustomerName.Should().Be(customer.Name);
        page.Items[0].ItemsCount.Should().Be(2);

        // Act 2: busca pelo número.
        var bySearch = await _fixture.GetAsync($"{ORDERS}?search=late-{tag}", Token);

        // Assert
        var found = bySearch.As<PagedResult<OrderInfo>>();
        found.Items.Should().ContainSingle();
        found.Items[0].OrderNumber.Should().StartWith("late-");

        // Act 3: página além do fim.
        var beyond = await _fixture.GetAsync($"{ORDERS}?page=999&pageSize=20", Token);

        // Assert
        beyond.As<PagedResult<OrderInfo>>().Items.Should().BeEmpty();
    }

    [Fact]
    public async Task GetById_ShouldReturnCustomerAndItems()
    {
        // Arrange: FR-016.
        var (customer, sale, _) = await NewSaleScenarioAsync();

        // Act
        var response = await _fixture.GetAsync($"{ORDERS}/{sale.Id}", Token);

        // Assert
        response.StatusCode.Should().Be(200);

        var detail = response.As<OrderDetailInfo>();
        detail.Id.Should().Be(sale.Id);
        detail.Customer.Id.Should().Be(customer.Id);
        detail.Customer.Name.Should().Be(customer.Name);

        // SC-005: mesmo no detalhe do pedido, o cliente aparece só com CPF mascarado.
        detail.Customer.CpfMasked.Should().StartWith("***.");
        response.Body.Should().NotContain(customer.Cpf);
        response.Body.Should().NotContain(customer.Cpf.Replace(".", string.Empty).Replace("-", string.Empty));

        detail.Items.Select(i => i.LineNo).Should().Equal(1, 2);
        detail.Items[0].Price.Should().Be(399.90m);
    }

    [Fact]
    public async Task Update_StatusAndTotal_ShouldReturn200()
    {
        // Arrange: FR-019.
        var (_, sale, _) = await NewSaleScenarioAsync();

        // Act
        var response = await _fixture.PutAsync($"{ORDERS}/{sale.Id}", Token,
            new OrderUpdateInfo { Status = "Faturado", Total = 579.80m });

        // Assert
        response.StatusCode.Should().Be(200);

        var updated = response.As<OrderDetailInfo>();
        updated.Total.Should().Be(579.80m);
        updated.Status.Should().Be("Faturado");

        (await _fixture.GetAsync($"{ORDERS}/{sale.Id}", Token))
            .As<OrderDetailInfo>().Total.Should().Be(579.80m);
    }

    [Fact]
    public async Task Update_TotalWithWrongSign_ShouldReturn400WithCampoTotal()
    {
        // Arrange: o kind gravado (sale) manda no sinal — T073/FR-019.
        var (_, sale, _) = await NewSaleScenarioAsync();

        // Act
        var response = await _fixture.PutAsync($"{ORDERS}/{sale.Id}", Token,
            new OrderUpdateInfo { Status = "Cancelado", Total = -589.80m });

        // Assert
        response.StatusCode.Should().Be(400);
        response.Body.Should().Contain("\"total\"");
        response.Body.Should().Contain("O total de uma venda deve ser positivo.");
    }

    [Fact]
    public async Task CustomerSummary_AfterOrders_ShouldReflectNetTotal()
    {
        // Arrange: Cenário 3 do quickstart — venda de 589,80 atualizada para 579,80 e um
        // cancelamento de -589,80: purchases = 1, netTotal = -10,00.
        var customer = await CreateCustomerAsync();
        var payload = TestDataHelper.NewSaleOrder(customer.Id);
        var sale = await CreateOrderAsync(payload);

        (await _fixture.PutAsync($"{ORDERS}/{sale.Id}", Token,
            new OrderUpdateInfo { Status = "Faturado", Total = 579.80m })).StatusCode.Should().Be(200);

        await CreateOrderAsync(TestDataHelper.NewCancellationOrder(customer.Id, payload.OrderNumber));

        // Act
        var response = await _fixture.GetAsync($"{CUSTOMERS}/{customer.Id}", Token);

        // Assert: FR-009 / research §10.
        response.StatusCode.Should().Be(200);

        var detail = response.As<CustomerDetailInfo>();
        detail.Summary.Purchases.Should().Be(1);
        detail.Summary.NetTotal.Should().Be(-10.00m);
        detail.Summary.LastPurchase.Should().Be(payload.OrderDate);
        detail.Summary.DaysSinceLastPurchase.Should().Be(
            DateOnly.FromDateTime(DateTime.UtcNow).DayNumber - payload.OrderDate.DayNumber);
    }
}
