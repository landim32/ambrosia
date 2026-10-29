using System.Text.Json;
using Ambrosio.ApiTests.Fixtures;
using Ambrosio.ApiTests.Helpers;
using Ambrosio.ApiTests.McpServer;
using Ambrosio.DTO.Common;
using Ambrosio.DTO.Customers;
using Ambrosio.DTO.Enums;
using Ambrosio.DTO.Imports;
using Ambrosio.DTO.Orders;
using Ambrosio.DTO.Users;
using FluentAssertions;

namespace Ambrosio.ApiTests.Controllers;

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
        // Arrange: A cadastra as credenciais do cenário e inicia uma simulação (não grava nada e termina
        // rápido). O início não leva credencial nenhuma — o cadastro é o do PUT (feature 008, FR-001).
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
        history.As<PagedResult<Ambrosio.DTO.Imports.ImportRunInfo>>().Items.Should().BeEmpty();

        (await _fixture.GetAsync("/imports/mais-vendas/runs/current", tokenB)).StatusCode.Should().Be(204);

        // A continua dona: a execução termina normalmente para ela.
        var finished = await _fixture.WaitUntilFinishedAsync(run.Id, tokenA);
        finished.Status.Should().Be(Ambrosio.DTO.Enums.ImportRunStatus.Succeeded);

        // E o que A importou não aparece para B (o vínculo da conta do +Vendas é por usuário, FR-008b).
        (await _fixture.CountOrdersAsync(tokenB)).Should().Be(0);
    }

    /// <summary>
    /// T036 (feature 008, FR-003/FR-004, constituição §VI): as credenciais de A não existem para B — B
    /// não as lê, não as usa para importar e não as apaga. A leitura é sempre pelo <c>sub</c> do token, e
    /// a única forma de B importar é cadastrar as próprias credenciais.
    /// </summary>
    [Fact]
    public async Task Credentials_OfSellerA_AreInvisibleAndUnusableBySellerB()
    {
        // Arrange: A cadastra login e senha; B não cadastra nada.
        var (tokenA, _) = await _fixture.NewSellerAsync("Ana Credenciais");
        var (tokenB, _) = await _fixture.NewSellerAsync("Bia Credenciais");
        var loginA = TestDataHelper.FakeLogin(TestDataHelper.SCENARIO_OK);

        await _fixture.SaveCredentialsAsync(tokenA, loginA, TestDataHelper.TRACEABLE_PASSWORD);

        // Assert 1 — B não vê: a própria leitura vem vazia, e nenhum corpo seu traz dado de A.
        var asB = await _fixture.GetCredentialsAsync(tokenB);

        asB.Login.Should().BeEmpty();
        asB.HasPassword.Should().BeFalse();
        asB.Status.Should().Be(MaisVendasCredentialsStatus.None);
        asB.SavedAt.Should().BeNull();

        var (bodyB, defaultsB) = await ReadCredentialSurfacesAsync(tokenB);

        bodyB.Should().NotContain(loginA);
        defaultsB.Should().NotContain(loginA);
        defaultsB.Should().NotContain(TestDataHelper.TRACEABLE_PASSWORD, "a senha de A não existe para B (FR-003)");

        (await _fixture.GetAsync("/imports/mais-vendas/defaults", tokenB))
            .As<ImportDefaultsInfo>().HasCredentials.Should().BeFalse();

        // Assert 2 — B não usa: sem cadastro próprio o início é recusado com a frase da tela, e nenhuma
        // execução nasce emprestada da conta de A.
        var refused = await _fixture.StartImportRawAsync(
            tokenB, login: null, password: null, TestDataHelper.ImportEnd, TestDataHelper.ImportEnd);

        refused.StatusCode.Should().Be(400, refused.ToString());
        refused.As<ErrorResult>().Erros.Should().ContainSingle(e =>
            e.Campo == "login"
            && e.Mensagem == "Cadastre as credenciais do +Vendas na configuração.");

        (await _fixture.GetAsync("/imports/mais-vendas/runs?page=1&pageSize=10", tokenB))
            .As<PagedResult<Ambrosio.DTO.Imports.ImportRunInfo>>().TotalCount.Should().Be(0);

        // Assert 3 — B não altera nem remove o cadastro de A: cada uma escreve só na própria linha.
        await _fixture.SaveCredentialsAsync(tokenB, "s777+ok", "senha-da-B");
        (await _fixture.DeleteCredentialsAsync(tokenB)).StatusCode.Should().Be(200);

        var asA = await _fixture.GetCredentialsAsync(tokenA);

        asA.Login.Should().Be(loginA);
        asA.HasPassword.Should().BeTrue();
        asA.Status.Should().Be(MaisVendasCredentialsStatus.Valid);
        asA.PasswordChangedAt.Should().NotBeNull("o cadastro de A continua no lugar depois de B mexer no dela");
    }

    /// <summary>
    /// T036 (feature 008, FR-016/R8): a execução iniciada pela conversa de A é da vendedora A — para B
    /// ela é o mesmo que uma execução que nunca existiu (404 com o corpo idêntico), inclusive quando foi
    /// criada por outro caminho que não a tela dela.
    /// </summary>
    [Fact]
    public async Task ImportRunStartedViaMcp_OfSellerA_IsInvisibleToSellerB()
    {
        // Arrange: A tem token da API e do plugin; B é uma vendedora comum, sem nada.
        var ana = await McpSeller.CreateAsync(_fixture, "Ana Mcp Isola", ImportToolsTests.ImportScopes);
        var (tokenB, _) = await _fixture.NewSellerAsync("Bia Isola Mcp");

        await _fixture.SaveCredentialsAsync(
            ana.User.ApiToken, TestDataHelper.FakeLogin(TestDataHelper.SCENARIO_OK), "v1");

        var started = await ana.CallAsync("iniciar_importacao", new
        {
            inicio = TestDataHelper.ImportEnd.ToString("yyyy-MM-dd"),
            fim = TestDataHelper.ImportEnd.ToString("yyyy-MM-dd"),
            simular = true,
            confirmar = true
        });

        var structured = WriteToolsTests.Structured(started);
        var runId = structured.GetProperty("registro").GetProperty("importacao_id").GetInt64();

        // Act: B tenta ler, cancelar e listar.
        var report = await _fixture.GetAsync($"/imports/mais-vendas/runs/{runId}", tokenB);
        var missing = await _fixture.GetAsync("/imports/mais-vendas/runs/999999999", tokenB);
        var cancel = await _fixture.PostAsync($"/imports/mais-vendas/runs/{runId}/cancel", tokenB, new { });

        // Assert: invisível nos três caminhos, e o histórico dela começa vazio.
        report.StatusCode.Should().Be(404);
        report.Body.Should().Be(missing.Body);
        cancel.StatusCode.Should().Be(404);
        (await _fixture.GetAsync("/imports/mais-vendas/runs?page=1&pageSize=50", tokenB))
            .As<PagedResult<Ambrosio.DTO.Imports.ImportRunInfo>>().Items.Should().BeEmpty();
        (await _fixture.GetAsync("/imports/mais-vendas/runs/current", tokenB)).StatusCode.Should().Be(204);

        // E a própria dona enxerga a execução da conversa, marcada como vinda do plugin (FR-016).
        var asOwner = await _fixture.GetAsync($"/imports/mais-vendas/runs/{runId}", ana.User.ApiToken);

        asOwner.StatusCode.Should().Be(200, asOwner.ToString());

        var detail = asOwner.As<Ambrosio.DTO.Imports.ImportRunDetailInfo>();

        detail.Id.Should().Be(runId);
        detail.StartedVia.Should().Be(Ambrosio.DTO.Enums.ImportRunStartedVia.Mcp);
        detail.Kind.Should().Be(Ambrosio.DTO.Enums.ImportRunKind.Period);

        await _fixture.WaitUntilFinishedAsync(runId, ana.User.ApiToken);
    }

    /// <summary>Lê o corpo do <c>GET /credentials</c> e do <c>GET /defaults</c> da vendedora.</summary>
    private async Task<(string CredentialsBody, string DefaultsBody)> ReadCredentialSurfacesAsync(string token)
    {
        var credentials = await _fixture.GetAsync("/imports/mais-vendas/credentials", token);
        var defaults = await _fixture.GetAsync("/imports/mais-vendas/defaults", token);

        return (credentials.Body, defaults.Body);
    }
}
