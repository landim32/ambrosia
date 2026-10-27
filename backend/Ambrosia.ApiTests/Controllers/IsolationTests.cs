using System.Text.Json;
using Ambrosia.ApiTests.Fixtures;
using Ambrosia.ApiTests.Helpers;
using Ambrosia.DTO.Common;
using Ambrosia.DTO.Customers;
using Ambrosia.DTO.Orders;
using Ambrosia.DTO.Users;
using FluentAssertions;

namespace Ambrosia.ApiTests.Controllers;

/// <summary>
/// T088 [US4]: isolamento de dados por usuário ponta a ponta (FR-021 a FR-023, SC-002). Com A e B
/// do fixture, B não lista, não lê, não altera e não vincula nada de A — e o corpo do 404 é
/// idêntico ao de um id inexistente, para não revelar a existência do recurso.
/// </summary>
[Collection(ApiTestCollection.API_TESTS)]
public class IsolationTests
{
    private const string CUSTOMERS = "/customers";
    private const string ORDERS = "/orders";

    private readonly ApiTestFixture _fixture;

    public IsolationTests(ApiTestFixture fixture) => _fixture = fixture;

    private string TokenA => _fixture.TokenA;

    private string TokenB => _fixture.TokenB;

    /// <summary>Setup do cenário: cliente + pedido de A, lidos antes das tentativas de B.</summary>
    private async Task<(CustomerDetailInfo Customer, OrderDetailInfo Order)> CreateOwnedDataAsync()
    {
        var customerResponse = await _fixture.PostAsync(CUSTOMERS, TokenA, TestDataHelper.NewCustomer());
        customerResponse.StatusCode.Should().Be(201, $"A deve conseguir cadastrar cliente: {customerResponse}");
        var customer = customerResponse.As<CustomerDetailInfo>();

        var orderResponse = await _fixture.PostAsync(
            ORDERS, TokenA, TestDataHelper.NewSaleOrder(customer.Id));
        orderResponse.StatusCode.Should().Be(201, $"A deve conseguir cadastrar pedido: {orderResponse}");

        return (customer, orderResponse.As<OrderDetailInfo>());
    }

    [Fact]
    public async Task ListCustomers_AsUserB_ShouldNotContainUserACustomers()
    {
        // Arrange
        var (customer, _) = await CreateOwnedDataAsync();

        // Act: B lista buscando pelo nome exato do cliente de A.
        var response = await _fixture.GetAsync(
            $"{CUSTOMERS}?search={Uri.EscapeDataString(customer.Name)}&page=1&pageSize=100", TokenB);

        // Assert: SC-002 — lista vazia, totalCount 0.
        response.StatusCode.Should().Be(200);
        response.Body.Should().NotContain(customer.Name);
        response.Body.Should().NotContain(customer.CpfMasked);

        using var document = JsonDocument.Parse(response.Body);
        document.RootElement.GetProperty("totalCount").GetInt32().Should().Be(0);
        document.RootElement.GetProperty("items").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task ListOrders_AsUserB_ShouldNotContainUserAOrders()
    {
        // Arrange
        var (customer, order) = await CreateOwnedDataAsync();

        // Act: B busca pelo número do pedido e pelo nome do cliente de A.
        var byNumber = await _fixture.GetAsync(
            $"{ORDERS}?search={Uri.EscapeDataString(order.OrderNumber)}", TokenB);
        var byCustomer = await _fixture.GetAsync(
            $"{ORDERS}?search={Uri.EscapeDataString(customer.Name)}", TokenB);
        var all = await _fixture.GetAsync($"{ORDERS}?customerId={customer.Id}", TokenB);

        // Assert
        foreach (var response in new[] { byNumber, byCustomer, all })
        {
            response.StatusCode.Should().Be(200);

            using var document = JsonDocument.Parse(response.Body);
            document.RootElement.GetProperty("items").GetArrayLength().Should().Be(0);
            document.RootElement.GetProperty("totalCount").GetInt32().Should().Be(0);
            response.Body.Should().NotContain(order.OrderNumber);
        }
    }

    [Fact]
    public async Task GetCustomer_OfUserA_AsUserB_ShouldReturn404()
    {
        // Arrange
        var (customer, _) = await CreateOwnedDataAsync();

        // Act
        var response = await _fixture.GetAsync($"{CUSTOMERS}/{customer.Id}", TokenB);

        // Assert: 404 (não 403 — não se revela que o recurso existe).
        response.StatusCode.Should().Be(404);
        response.Body.Should().NotContain(customer.Name);
        response.Body.Should().NotContain(customer.Cpf);
    }

    [Fact]
    public async Task UpdateCustomer_OfUserA_AsUserB_ShouldReturn404AndKeepName()
    {
        // Arrange
        var (customer, _) = await CreateOwnedDataAsync();

        // Act: B tenta renomear o cliente de A.
        var attack = await _fixture.PutAsync($"{CUSTOMERS}/{customer.Id}", TokenB,
            new CustomerUpdateInfo { Name = "Sequestrado por B" });

        // Assert
        attack.StatusCode.Should().Be(404);

        var asOwner = await _fixture.GetAsync($"{CUSTOMERS}/{customer.Id}", TokenA);
        asOwner.As<CustomerDetailInfo>().Name.Should().Be(customer.Name);
    }

    [Fact]
    public async Task GetOrder_OfUserA_AsUserB_ShouldReturn404()
    {
        // Arrange
        var (_, order) = await CreateOwnedDataAsync();

        // Act
        var response = await _fixture.GetAsync($"{ORDERS}/{order.Id}", TokenB);

        // Assert
        response.StatusCode.Should().Be(404);
        response.Body.Should().NotContain(order.OrderNumber);
    }

    [Fact]
    public async Task UpdateOrder_OfUserA_AsUserB_ShouldReturn404AndKeepTotal()
    {
        // Arrange
        var (_, order) = await CreateOwnedDataAsync();

        // Act: B tenta zerar o total do pedido de A.
        var attack = await _fixture.PutAsync($"{ORDERS}/{order.Id}", TokenB,
            new OrderUpdateInfo { Status = "Cancelado por B", Total = 1.00m });

        // Assert
        attack.StatusCode.Should().Be(404);

        var asOwner = await _fixture.GetAsync($"{ORDERS}/{order.Id}", TokenA);
        var intact = asOwner.As<OrderDetailInfo>();
        intact.Total.Should().Be(order.Total);
        intact.Status.Should().Be(order.Status);
    }

    [Fact]
    public async Task CreateOrder_WithUserACustomer_AsUserB_ShouldReturn404()
    {
        // Arrange: FR-023 — B conhece o customerId de A (id sequencial) e tenta vincular.
        var (customer, _) = await CreateOwnedDataAsync();
        var payload = TestDataHelper.NewSaleOrder(customer.Id);

        // Act
        var response = await _fixture.PostAsync(ORDERS, TokenB, payload);

        // Assert: a recusa é 404 "Cliente não encontrado." — a mesma mensagem do caso inexistente.
        response.StatusCode.Should().Be(404);

        using var document = JsonDocument.Parse(response.Body);
        document.RootElement.GetProperty("mensagem").GetString().Should().Be("Cliente não encontrado.");

        // E nenhum pedido de B foi criado com aquele cliente.
        var asOwner = await _fixture.GetAsync($"{ORDERS}?customerId={customer.Id}", TokenB);
        asOwner.As<PagedResult<OrderInfo>>().Items.Should().BeEmpty();
    }

    [Fact]
    public async Task SameCpf_ForUsersAAndB_ShouldCreateTwoIndependentCustomers()
    {
        // Arrange: US4 cenário 4 / quickstart — a unicidade é (user_id, cpf_hash).
        var cpf = TestDataHelper.GenerateValidCpf();

        var first = await _fixture.PostAsync(CUSTOMERS, TokenA,
            new CustomerInsertInfo { Name = "Maria (de Ana)", Cpf = cpf });
        var second = await _fixture.PostAsync(CUSTOMERS, TokenB,
            new CustomerInsertInfo { Name = "Maria (de Bia)", Cpf = cpf });

        // Assert: os dois cadastros passam, com ids diferentes.
        first.StatusCode.Should().Be(201);
        second.StatusCode.Should().Be(201);

        var fromA = first.As<CustomerDetailInfo>();
        var fromB = second.As<CustomerDetailInfo>();

        fromA.Id.Should().NotBe(fromB.Id);
        fromA.Cpf.Should().Be(fromB.Cpf, "o mesmo documento é legível por ambas");

        // Cada uma enxerga apenas o próprio registro.
        (await _fixture.GetAsync($"{CUSTOMERS}/{fromA.Id}", TokenB)).StatusCode.Should().Be(404);
        (await _fixture.GetAsync($"{CUSTOMERS}/{fromB.Id}", TokenA)).StatusCode.Should().Be(404);

        // E a duplicidade continua valendo dentro do mesmo usuário.
        (await _fixture.PostAsync(CUSTOMERS, TokenA,
            new CustomerInsertInfo { Name = "Outra de novo", Cpf = cpf })).StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task NotFoundBody_ForOtherUserResource_ShouldEqualBodyForNonexistentId()
    {
        // Arrange: FR-022 — a resposta não pode distinguir "não existe" de "é de outro usuário".
        var (customer, order) = await CreateOwnedDataAsync();

        // Act: B lê o cliente e o pedido de A, e um id jamais criado.
        var ofOtherUser = await _fixture.GetAsync($"{CUSTOMERS}/{customer.Id}", TokenB);
        var nonexistent = await _fixture.GetAsync($"{CUSTOMERS}/{long.MaxValue}", TokenB);
        var orderOfOtherUser = await _fixture.GetAsync($"{ORDERS}/{order.Id}", TokenB);
        var orderNonexistent = await _fixture.GetAsync($"{ORDERS}/{long.MaxValue}", TokenB);

        // Assert: status e corpo idênticos par a par.
        ofOtherUser.StatusCode.Should().Be(nonexistent.StatusCode).And.Be(404);
        ofOtherUser.Body.Should().Be(nonexistent.Body);

        orderOfOtherUser.StatusCode.Should().Be(orderNonexistent.StatusCode).And.Be(404);
        orderOfOtherUser.Body.Should().Be(orderNonexistent.Body);
    }

    [Fact]
    public async Task UserProfile_AsBothUsers_ShouldNeverBeTheOthers()
    {
        // Arrange/Act: cada token resolve o próprio GET /users/me.
        var asA = (await _fixture.GetAsync("/users/me", TokenA)).As<UserInfo>();
        var asB = (await _fixture.GetAsync("/users/me", TokenB)).As<UserInfo>();

        // Assert: os dois são o A/B do fixture (base de todo o isolamento acima).
        asA.Id.Should().Be(_fixture.UserIdA);
        asB.Id.Should().Be(_fixture.UserIdB);
        asA.Id.Should().NotBe(asB.Id);
    }

    /// <summary>
    /// T092 [US7] (FR-053): a execução de importação de A não existe para B — nem no relatório, nem no
    /// cancelamento, nem no histórico, nem como "execução atual". Vendedoras novas por teste, para o
    /// histórico de B começar vazio.
    /// </summary>
    [Fact]
    public async Task ImportRun_OfSellerA_IsInvisibleToSellerB()
    {
        // Arrange: A inicia uma simulação (não grava nada e termina rápido).
        var (tokenA, _) = await _fixture.NewSellerAsync("Ana Importa");
        var (tokenB, _) = await _fixture.NewSellerAsync("Bia Isola");

        var run = await _fixture.StartImportAsync(
            tokenA,
            TestDataHelper.FakeLogin(TestDataHelper.SCENARIO_OK),
            "v1",
            TestDataHelper.ImportEnd,
            TestDataHelper.ImportEnd,
            dryRun: true);

        // Act + Assert: B recebe 404 idêntico ao de execução inexistente.
        var report = await _fixture.GetAsync($"/imports/mais-vendas/runs/{run.Id}", tokenB);
        var missing = await _fixture.GetAsync("/imports/mais-vendas/runs/999999999", tokenB);

        report.StatusCode.Should().Be(404);
        report.Body.Should().Be(missing.Body);

        var cancel = await _fixture.PostAsync($"/imports/mais-vendas/runs/{run.Id}/cancel", tokenB, new { });
        cancel.StatusCode.Should().Be(404);

        var history = await _fixture.GetAsync("/imports/mais-vendas/runs?page=1&pageSize=50", tokenB);
        history.StatusCode.Should().Be(200);
        history.As<PagedResult<Ambrosia.DTO.Imports.ImportRunInfo>>().Items.Should().BeEmpty();

        (await _fixture.GetAsync("/imports/mais-vendas/runs/current", tokenB)).StatusCode.Should().Be(204);

        // A continua dona: a execução termina normalmente para ela.
        var finished = await _fixture.WaitUntilFinishedAsync(run.Id, tokenA);
        finished.Status.Should().Be(Ambrosia.DTO.Enums.ImportRunStatus.Succeeded);
    }
}
