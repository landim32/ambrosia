using System.Text.RegularExpressions;
using Ambrosia.ApiTests.Fixtures;
using Ambrosia.ApiTests.Helpers;
using Ambrosia.DTO.Common;
using Ambrosia.DTO.Customers;
using Ambrosia.DTO.Enums;
using Ambrosia.DTO.Imports;
using Ambrosia.DTO.Orders;
using FluentAssertions;

namespace Ambrosia.ApiTests.Controllers;

/// <summary>
/// Importação do +Vendas (feature 006, T036/T061/T069/T078/T083/T087/T091) contra a homologação com
/// <c>MaisVendas:Collector=Fake</c> (research §3/§16). Cada teste cria a <b>própria vendedora</b>: a
/// idempotência faria um teste herdar os pedidos do outro, e nenhuma contagem seria conferível.
/// <para>
/// Logins do fake: <c>conta+cenario</c>; senha = versão do conteúdo (<c>v1</c>/<c>v2</c>/<c>v3</c>) ou
/// <c>recusada</c>. Nenhum valor real de login/senha do +Vendas aparece aqui.
/// </para>
/// </summary>
[Collection(ApiTestCollection.API_TESTS)]
public class ImportsControllerTests
{
    private const string RUNS = "/imports/mais-vendas/runs";
    private const string DEFAULTS = "/imports/mais-vendas/defaults";

    private static readonly DateOnly Start = TestDataHelper.ImportStart;
    private static readonly DateOnly End = TestDataHelper.ImportEnd;

    /// <summary>Bloco 3 do período de referência (o que falha em <c>falha-bloco3</c>).</summary>
    private static readonly DateOnly Block3Start = new(2026, 10, 10);
    private static readonly DateOnly Block3End = new(2026, 10, 16);

    /// <summary>CPF completo, pontuado ou não — só a máscara pode aparecer numa resposta (FR-052).</summary>
    private static readonly Regex FullCpf = new(@"(?<![\d*])\d{3}\.?\d{3}\.?\d{3}-?\d{2}(?!\d)", RegexOptions.Compiled);

    private readonly ApiTestFixture _fixture;

    public ImportsControllerTests(ApiTestFixture fixture) => _fixture = fixture;

    // =====================================================================================
    // Helpers
    // =====================================================================================

    private static string Login(string scenario, string? account = null) =>
        TestDataHelper.FakeLogin(scenario, account);

    /// <summary>Inicia e espera o fim; devolve o relatório final.</summary>
    private async Task<ImportRunDetailInfo> RunAsync(
        string token,
        string scenario,
        string password = "v1",
        DateOnly? start = null,
        DateOnly? end = null,
        bool dryRun = false,
        string? account = null)
    {
        var run = await _fixture.StartImportAsync(
            token, Login(scenario, account), password, start ?? Start, end ?? End, dryRun);

        return await _fixture.WaitUntilFinishedAsync(run.Id, token);
    }

    private async Task<List<OrderInfo>> ListAllOrdersAsync(string token)
    {
        var response = await _fixture.GetAsync("/orders?page=1&pageSize=100", token);

        response.StatusCode.Should().Be(200, response.ToString());

        return response.As<PagedResult<OrderInfo>>().Items;
    }

    private async Task<OrderDetailInfo> GetOrderAsync(string token, long orderId)
    {
        var response = await _fixture.GetAsync($"/orders/{orderId}", token);

        response.StatusCode.Should().Be(200, response.ToString());

        return response.As<OrderDetailInfo>();
    }

    private static int OrdersSeen(ImportRunCountsInfo counts) =>
        counts.Inserted + counts.Updated + counts.Unchanged;

    // =====================================================================================
    // US1 — importar um período e acompanhar
    // =====================================================================================

    [Fact]
    public async Task Defaults_NewSeller_ReturnsNullStartAndToday()
    {
        var (token, _) = await _fixture.NewSellerAsync();

        var response = await _fixture.GetAsync(DEFAULTS, token);

        response.StatusCode.Should().Be(200, response.ToString());

        var defaults = response.As<ImportDefaultsInfo>();

        defaults.SuggestedStartDate.Should().BeNull();
        defaults.SuggestedEndDate.Should().BeOnOrAfter(End);
        defaults.BlockDays.Should().Be(_fixture.MaisVendasBlockDays);
        defaults.MaxRangeDays.Should().Be(_fixture.MaisVendasMaxRangeDays);
        defaults.LockedUntil.Should().BeNull();
    }

    [Fact]
    public async Task Defaults_AfterImport_ReturnsLastImportedOrderDate()
    {
        var (token, _) = await _fixture.NewSellerAsync();

        await RunAsync(token, TestDataHelper.SCENARIO_OK, start: Block3Start, end: Block3End);

        var defaults = (await _fixture.GetAsync(DEFAULTS, token)).As<ImportDefaultsInfo>();

        defaults.SuggestedStartDate.Should().Be(Block3End);
    }

    [Theory]
    [InlineData("2026-10-10", "2026-10-09", "s482+ok", "v1", "startDate", "A data inicial deve ser anterior ou igual à data final.")]
    [InlineData("2026-10-10", "2099-01-01", "s482+ok", "v1", "endDate", "A data final não pode estar no futuro.")]
    [InlineData("2026-10-10", "2026-10-16", "", "v1", "login", "Informe o login do +Vendas.")]
    [InlineData("2026-10-10", "2026-10-16", "s482+ok", "", "password", "Informe a senha do +Vendas.")]
    public async Task Start_Invalid_Returns400(
        string startDate, string endDate, string login, string password, string field, string message)
    {
        var (token, _) = await _fixture.NewSellerAsync();

        var response = await _fixture.PostAsync(RUNS, token, new { login, password, startDate, endDate, dryRun = false });

        response.StatusCode.Should().Be(400, response.ToString());

        var error = response.As<ErrorResult>();

        error.Erros.Should().Contain(e => e.Campo == field && e.Mensagem == message);

        // FR-006: nenhuma mensagem ecoa o valor enviado.
        if (login.Length > 0)
        {
            response.Body.Should().NotContain(login);
        }
    }

    [Fact]
    public async Task Start_RangeAboveLimit_Returns400WithLimit()
    {
        var (token, _) = await _fixture.NewSellerAsync();
        var end = End;
        var start = end.AddDays(-_fixture.MaisVendasMaxRangeDays); // limite + 1 dia, inclusivo

        var response = await _fixture.StartImportRawAsync(token, Login("ok"), "v1", start, end);

        response.StatusCode.Should().Be(400, response.ToString());
        response.As<ErrorResult>().Erros.Should().Contain(e =>
            e.Campo == "endDate"
            && e.Mensagem == $"O período não pode ultrapassar {_fixture.MaisVendasMaxRangeDays} dias.");
    }

    [Fact]
    public async Task Start_Valid_Returns202QueuedWith5Blocks()
    {
        var (token, _) = await _fixture.NewSellerAsync();

        var response = await _fixture.StartImportRawAsync(token, Login("ok"), "v1", Start, End);

        response.StatusCode.Should().Be(202, response.ToString());

        var run = response.As<ImportRunInfo>();

        run.Status.Should().Be(ImportRunStatus.Queued);
        run.Mode.Should().Be(ImportRunMode.Apply);
        run.BlocksTotal.Should().Be(5);
        run.BlockDays.Should().Be(_fixture.MaisVendasBlockDays);

        await _fixture.WaitUntilFinishedAsync(run.Id, token);
    }

    [Fact]
    public async Task Run_OkScenario_Succeeds_With49Orders()
    {
        var (token, _) = await _fixture.NewSellerAsync();

        var report = await RunAsync(token, TestDataHelper.SCENARIO_OK);

        report.Status.Should().Be(ImportRunStatus.Succeeded, report.ErrorMessage);
        report.Blocks.Should().HaveCount(5).And.OnlyContain(b => b.Status == ImportBlockStatus.Succeeded);
        report.Counts.Inserted.Should().Be(TestDataHelper.IMPORT_ORDER_COUNT);
        report.Counts.Failed.Should().Be(0);
        report.DurationSeconds.Should().NotBeNull();

        report.Changes.Should().HaveCount(TestDataHelper.IMPORT_ORDER_COUNT)
            .And.OnlyContain(c => c.Action == ImportChangeAction.Inserted && c.OrderId != null);

        report.Breakdown.Should().NotBeEmpty();
        report.Breakdown.Sum(b => b.Count).Should().Be(TestDataHelper.IMPORT_ORDER_COUNT);
        report.Breakdown.Should().Contain(b =>
            b.Channel == "Linx" && b.Origin == OrderOrigin.Cota && b.Kind == OrderKind.Sale
            && b.Action == ImportChangeAction.Inserted);

        var orders = await ListAllOrdersAsync(token);

        orders.Should().HaveCount(TestDataHelper.IMPORT_ORDER_COUNT);
        orders.Sum(o => o.Total).Should().Be(TestDataHelper.IMPORT_NET_TOTAL);
        orders.Should().OnlyContain(o => o.Source == OrderSource.MaisVendas);
    }

    [Fact]
    public async Task Run_CancelledSale_CreatesSaleAndCancellation()
    {
        var (token, _) = await _fixture.NewSellerAsync();

        await RunAsync(token, TestDataHelper.SCENARIO_OK, start: Block3Start, end: Block3End);

        var pair = (await ListAllOrdersAsync(token)).Where(o => o.OrderNumber == "41900026").ToList();

        pair.Should().HaveCount(2);
        pair.Should().ContainSingle(o => o.Kind == OrderKind.Sale && o.Total == 2450.00m);
        pair.Should().ContainSingle(o => o.Kind == OrderKind.Cancellation && o.Total == -2450.00m);
    }

    [Fact]
    public async Task Run_SameNumberDifferentDates_CreatesTwoOrders()
    {
        var (token, _) = await _fixture.NewSellerAsync();

        await RunAsync(token, TestDataHelper.SCENARIO_OK, end: new DateOnly(2026, 10, 9));

        var same = (await ListAllOrdersAsync(token)).Where(o => o.OrderNumber == "41900001").ToList();

        same.Select(o => o.OrderDate).Should().BeEquivalentTo(
            [new DateOnly(2026, 9, 28), new DateOnly(2026, 10, 5)]);
    }

    [Fact]
    public async Task Run_SingleDayPeriod_CreatesOneBlock()
    {
        var (token, _) = await _fixture.NewSellerAsync();

        var report = await RunAsync(token, TestDataHelper.SCENARIO_OK, start: End, end: End);

        report.BlocksTotal.Should().Be(1);
        report.Blocks.Should().ContainSingle();
        report.Status.Should().Be(ImportRunStatus.Succeeded);
        report.Counts.Inserted.Should().Be(3); // 25/10: venda + cancelamento de 41900045 e 41900046
    }

    // =====================================================================================
    // US2 — credenciais nunca guardadas
    // =====================================================================================

    [Fact]
    public async Task Responses_NeverContainCredentials()
    {
        var (token, _) = await _fixture.NewSellerAsync();
        var login = Login("ok");
        const string password = TestDataHelper.TRACEABLE_PASSWORD;

        var bodies = new List<string>();

        var start = await _fixture.StartImportRawAsync(token, login, password, End, End);
        bodies.Add(start.Body);

        // 409 com a execução ainda ativa (ou 202 se o fake já terminou — o corpo é conferido igual).
        bodies.Add((await _fixture.StartImportRawAsync(token, login, password, End, End)).Body);

        // 400: período invertido, com as credenciais no corpo enviado.
        bodies.Add((await _fixture.StartImportRawAsync(token, login, password, End, Start)).Body);

        var runId = start.As<ImportRunInfo>().Id;
        await _fixture.WaitUntilFinishedAsync(runId, token);

        bodies.Add((await _fixture.GetAsync($"{RUNS}/{runId}", token)).Body);
        bodies.Add((await _fixture.GetAsync($"{RUNS}?page=1&pageSize=50", token)).Body);
        bodies.Add((await _fixture.GetAsync(DEFAULTS, token)).Body);
        bodies.Add((await _fixture.PostAsync($"{RUNS}/{runId}/cancel", token, new { })).Body);

        foreach (var body in bodies)
        {
            body.Should().NotContain(password);
            body.Should().NotContain(login);
            body.Should().NotContainEquivalentOf("accountHash");
            body.Should().NotContainEquivalentOf("\"password\"");
        }
    }

    [Fact]
    public async Task Run_RefusedPassword_FailsWithMessageAndWritesNothing()
    {
        var (token, _) = await _fixture.NewSellerAsync();

        var report = await RunAsync(token, TestDataHelper.SCENARIO_OK, password: "recusada");

        report.Status.Should().Be(ImportRunStatus.Failed);
        report.ErrorMessage.Should().Be("Credenciais recusadas pelo +Vendas.");
        report.Blocks.Should().OnlyContain(b => b.Status == ImportBlockStatus.NotExecuted);
        (await _fixture.CountOrdersAsync(token)).Should().Be(0);
    }

    [Fact]
    public async Task Start_AfterThreeRefusals_Returns429WithRetryAfter()
    {
        var (token, _) = await _fixture.NewSellerAsync();

        for (var i = 0; i < _fixture.MaisVendasMaxFailedLogins; i++)
        {
            (await RunAsync(token, TestDataHelper.SCENARIO_OK, password: "recusada", start: End, end: End))
                .Status.Should().Be(ImportRunStatus.Failed);
        }

        var blocked = await _fixture.StartImportRawAsync(token, Login("ok"), "v1", End, End);

        blocked.StatusCode.Should().Be(429, blocked.ToString());
        blocked.As<ErrorResult>().Mensagem.Should().StartWith("Muitas tentativas com credenciais recusadas.");

        var retryAfter = blocked.Header("Retry-After");
        retryAfter.Should().NotBeNullOrEmpty();
        int.TryParse(retryAfter, out var seconds).Should().BeTrue("Retry-After é em segundos, não horário");
        seconds.Should().BeInRange(1, _fixture.MaisVendasLockoutMinutes * 60);

        var defaults = (await _fixture.GetAsync(DEFAULTS, token)).As<ImportDefaultsInfo>();
        defaults.LockedUntil.Should().NotBeNull();
    }

    [Fact]
    public async Task Run_AcceptedLogin_ResetsRefusalCount()
    {
        var (token, _) = await _fixture.NewSellerAsync();

        for (var i = 0; i < _fixture.MaisVendasMaxFailedLogins - 1; i++)
        {
            await RunAsync(token, TestDataHelper.SCENARIO_OK, password: "recusada", start: End, end: End);
        }

        (await RunAsync(token, TestDataHelper.SCENARIO_OK, start: End, end: End, dryRun: true))
            .Status.Should().Be(ImportRunStatus.Succeeded);

        // Uma nova recusa depois do aceite não bloqueia: a contagem recomeçou.
        await RunAsync(token, TestDataHelper.SCENARIO_OK, password: "recusada", start: End, end: End);

        var next = await _fixture.StartImportRawAsync(token, Login("ok"), "v1", End, End, dryRun: true);
        next.StatusCode.Should().Be(202, next.ToString());

        await _fixture.WaitUntilFinishedAsync(next.As<ImportRunInfo>().Id, token);
    }

    [Fact]
    public async Task Run_DifferentAccount_FailsBeforeWriting()
    {
        var (token, _) = await _fixture.NewSellerAsync();

        await RunAsync(token, TestDataHelper.SCENARIO_OK, start: End, end: End);
        var before = await _fixture.CountOrdersAsync(token);

        var report = await RunAsync(token, TestDataHelper.SCENARIO_OK, account: "s999");

        report.Status.Should().Be(ImportRunStatus.Failed);
        report.ErrorMessage.Should().Be("Conta do +Vendas diferente da vinculada.");
        (await _fixture.CountOrdersAsync(token)).Should().Be(before);
    }

    [Fact]
    public async Task DryRun_DifferentAccount_AlsoFails()
    {
        var (token, _) = await _fixture.NewSellerAsync();

        await RunAsync(token, TestDataHelper.SCENARIO_OK, start: End, end: End);

        var report = await RunAsync(token, TestDataHelper.SCENARIO_OK, account: "s999", dryRun: true);

        report.Status.Should().Be(ImportRunStatus.Failed);
        report.ErrorMessage.Should().Be("Conta do +Vendas diferente da vinculada.");
    }

    [Fact]
    public async Task Run_TwoFactor_FailsWithUnsupportedStep()
    {
        var (token, _) = await _fixture.NewSellerAsync();

        var report = await RunAsync(token, "2fa");

        report.Status.Should().Be(ImportRunStatus.Failed);
        report.ErrorMessage.Should().Be("O login do +Vendas exige uma etapa não suportada (código ou captcha).");
        (await _fixture.CountOrdersAsync(token)).Should().Be(0);
    }

    // =====================================================================================
    // US3 — repetir sem duplicar
    // =====================================================================================

    [Fact]
    public async Task Reimport_SameContent_AllUnchanged()
    {
        var (token, _) = await _fixture.NewSellerAsync();

        await RunAsync(token, TestDataHelper.SCENARIO_OK);
        var customers = await _fixture.CountCustomersAsync(token);

        var again = await RunAsync(token, TestDataHelper.SCENARIO_OK);

        again.Status.Should().Be(ImportRunStatus.Succeeded);
        again.Counts.Unchanged.Should().Be(TestDataHelper.IMPORT_ORDER_COUNT);
        again.Counts.Inserted.Should().Be(0);
        again.Counts.Updated.Should().Be(0);
        again.Counts.CustomersCreated.Should().Be(0);
        again.Counts.ImagesStored.Should().Be(0);
        (await _fixture.CountOrdersAsync(token)).Should().Be(TestDataHelper.IMPORT_ORDER_COUNT);
        (await _fixture.CountCustomersAsync(token)).Should().Be(customers);
    }

    [Fact]
    public async Task Reimport_V2_UpdatesWithChangedFields()
    {
        var (token, _) = await _fixture.NewSellerAsync();

        await RunAsync(token, TestDataHelper.SCENARIO_OK);
        var report = await RunAsync(token, TestDataHelper.SCENARIO_OK, password: "v2");

        // 6 pedidos com valor/status alterados + 2 pedidos do cliente renomeado no v2 (o 41900001 nas
        // duas datas; o 41900004, do mesmo cliente, já está entre os 6) — o nome entra no hash.
        const int expectedUpdated = 8;

        report.Counts.Updated.Should().Be(expectedUpdated);
        report.Counts.Unchanged.Should().Be(TestDataHelper.IMPORT_ORDER_COUNT - expectedUpdated);

        var updated22 = report.Changes.Single(c => c.OrderNumber == "41900022");
        updated22.Action.Should().Be(ImportChangeAction.Updated);
        updated22.ChangedFields.Should().Contain("total").And.Contain("status");

        var order = (await ListAllOrdersAsync(token)).Single(o => o.OrderNumber == "41900022");
        order.Total.Should().Be(2950.00m);
        order.Status.Should().Be("Entregue");
    }

    [Fact]
    public async Task Reimport_AfterManualEdit_RevertsToSource()
    {
        var (token, _) = await _fixture.NewSellerAsync();

        await RunAsync(token, TestDataHelper.SCENARIO_OK, start: End, end: End);
        var order = (await ListAllOrdersAsync(token)).Single(o => o.OrderNumber == "41900046");

        var edit = await _fixture.PutAsync($"/orders/{order.Id}", token, new OrderUpdateInfo
        {
            Status = "Editado à mão",
            Total = 10.00m
        });
        edit.StatusCode.Should().Be(200, edit.ToString());

        var report = await RunAsync(token, TestDataHelper.SCENARIO_OK, start: End, end: End);

        var change = report.Changes.Single(c => c.OrderNumber == "41900046");
        change.Action.Should().Be(ImportChangeAction.Updated);
        change.ChangedFields.Should().Contain("total");

        (await GetOrderAsync(token, order.Id)).Total.Should().Be(6090.28m);
    }

    [Fact]
    public async Task Run_FailureInBlock3_Keeps14OrdersAndStops()
    {
        var (token, _) = await _fixture.NewSellerAsync();

        var report = await RunAsync(token, "falha-bloco3");

        report.Status.Should().Be(ImportRunStatus.Failed);
        report.Blocks.Select(b => b.Status).Should().Equal(
            ImportBlockStatus.Succeeded,
            ImportBlockStatus.Succeeded,
            ImportBlockStatus.Failed,
            ImportBlockStatus.NotExecuted,
            ImportBlockStatus.NotExecuted);

        var block3 = report.Blocks.Single(b => b.BlockNo == 3);
        OrdersSeen(block3.Counts).Should().Be(14);
        block3.ErrorMessage.Should().NotBeNullOrWhiteSpace();

        (await _fixture.CountOrdersAsync(token)).Should().Be(8 + 8 + 14);
    }

    [Fact]
    public async Task RepeatBlock3_ThenFullPeriod_MatchesCleanRun()
    {
        var (token, _) = await _fixture.NewSellerAsync();

        await RunAsync(token, "falha-bloco3");

        (await RunAsync(token, TestDataHelper.SCENARIO_OK, start: Block3Start, end: Block3End))
            .Status.Should().Be(ImportRunStatus.Succeeded);

        var full = await RunAsync(token, TestDataHelper.SCENARIO_OK);
        full.Status.Should().Be(ImportRunStatus.Succeeded);

        var orders = await ListAllOrdersAsync(token);

        orders.Should().HaveCount(TestDataHelper.IMPORT_ORDER_COUNT);
        orders.Sum(o => o.Total).Should().Be(TestDataHelper.IMPORT_NET_TOTAL);
        orders.GroupBy(o => (o.OrderNumber, o.OrderDate, o.Kind)).Should().OnlyContain(g => g.Count() == 1);
    }

    [Theory]
    [InlineData("contador-divergente")]
    [InlineData("total-divergente")]
    public async Task Run_SourceMismatch_FailsBlockWithoutWriting(string scenario)
    {
        var (token, _) = await _fixture.NewSellerAsync();

        var report = await RunAsync(token, scenario);

        report.Status.Should().Be(ImportRunStatus.Failed);
        report.Blocks[0].Status.Should().Be(ImportBlockStatus.Failed);
        report.Blocks[0].ErrorMessage.Should().NotBeNullOrWhiteSpace();
        (await _fixture.CountOrdersAsync(token)).Should().Be(0);
    }

    [Fact]
    public async Task Reimport_V3_ReportsMissingWithoutDeleting()
    {
        var (token, _) = await _fixture.NewSellerAsync();

        await RunAsync(token, TestDataHelper.SCENARIO_OK);
        var report = await RunAsync(token, TestDataHelper.SCENARIO_OK, password: "v3");

        report.Counts.Missing.Should().Be(1);
        report.Changes.Should().ContainSingle(c => c.Action == ImportChangeAction.Missing)
            .Which.OrderNumber.Should().Be("41900013");
        (await _fixture.CountOrdersAsync(token)).Should().Be(TestDataHelper.IMPORT_ORDER_COUNT);
    }

    [Fact]
    public async Task Run_ManualOrderSameKey_ReportsConflictAndPartial()
    {
        var (token, _) = await _fixture.NewSellerAsync();

        var customer = (await _fixture.PostAsync("/customers", token, TestDataHelper.NewCustomer())).As<CustomerInfo>();
        var manual = TestDataHelper.NewSaleOrder(customer.Id, "41900046");
        manual.OrderDate = End;
        var created = await _fixture.PostAsync("/orders", token, manual);
        created.StatusCode.Should().Be(201, created.ToString());

        var report = await RunAsync(token, TestDataHelper.SCENARIO_OK, start: End, end: End);

        report.Status.Should().Be(ImportRunStatus.Partial);
        report.Counts.Conflicts.Should().Be(1);
        report.Changes.Single(c => c.OrderNumber == "41900046").Action.Should().Be(ImportChangeAction.Conflict);

        var kept = await GetOrderAsync(token, created.As<OrderDetailInfo>().Id);
        kept.Total.Should().Be(TestDataHelper.SALE_TOTAL);
        kept.Source.Should().Be(OrderSource.Manual);
    }

    [Fact]
    public async Task Run_YearlessDates_ResolvedByBlock()
    {
        var (token, _) = await _fixture.NewSellerAsync();

        var report = await RunAsync(token, "sem-ano");

        report.Status.Should().Be(ImportRunStatus.Succeeded, report.ErrorMessage);
        (await ListAllOrdersAsync(token)).Should().OnlyContain(o => o.OrderDate >= Start && o.OrderDate <= End);
    }

    [Fact]
    public async Task Run_UnstableSource_RetriesAndSucceeds()
    {
        var (token, _) = await _fixture.NewSellerAsync();

        var report = await RunAsync(token, "instavel", start: End, end: End);

        report.Status.Should().Be(ImportRunStatus.Succeeded, report.ErrorMessage);
    }

    [Fact]
    public async Task Run_EmptyPeriod_SucceedsWithZeroCounts()
    {
        var (token, _) = await _fixture.NewSellerAsync();

        var report = await RunAsync(token, "vazio");

        report.Status.Should().Be(ImportRunStatus.Succeeded);
        OrdersSeen(report.Counts).Should().Be(0);
    }

    // =====================================================================================
    // US4 — simulação
    // =====================================================================================

    [Fact]
    public async Task DryRun_NewSeller_WritesNothing()
    {
        var (token, _) = await _fixture.NewSellerAsync();

        var report = await RunAsync(token, TestDataHelper.SCENARIO_OK, dryRun: true);

        report.Mode.Should().Be(ImportRunMode.DryRun);
        report.Status.Should().Be(ImportRunStatus.Succeeded);
        (await _fixture.CountOrdersAsync(token)).Should().Be(0);
        (await _fixture.CountCustomersAsync(token)).Should().Be(0);
        (await _fixture.GetAsync(DEFAULTS, token)).As<ImportDefaultsInfo>().SuggestedStartDate.Should().BeNull();
    }

    [Fact]
    public async Task DryRun_ReportShowsWouldInsertCustomersAndImages()
    {
        var (token, _) = await _fixture.NewSellerAsync();

        var report = await RunAsync(token, TestDataHelper.SCENARIO_OK, dryRun: true);

        report.Counts.Inserted.Should().Be(TestDataHelper.IMPORT_ORDER_COUNT);
        report.Counts.CustomersCreated.Should().Be(12);
        report.Counts.ImagesStored.Should().Be(4);
        report.Changes.Should().OnlyContain(c => c.OrderId == null);
    }

    [Fact]
    public async Task DryRun_CounterMismatch_FailsLikeApply()
    {
        var (token, _) = await _fixture.NewSellerAsync();

        var report = await RunAsync(token, "contador-divergente", dryRun: true);

        report.Status.Should().Be(ImportRunStatus.Failed);
        report.Blocks[0].Status.Should().Be(ImportBlockStatus.Failed);
    }

    [Fact]
    public async Task DryRun_DoesNotLinkAccount()
    {
        var (token, _) = await _fixture.NewSellerAsync();

        await RunAsync(token, TestDataHelper.SCENARIO_OK, start: End, end: End, dryRun: true);

        // Sem vínculo, outra conta em modo gravação é aceita.
        (await RunAsync(token, TestDataHelper.SCENARIO_OK, start: End, end: End, account: "s999"))
            .Status.Should().Be(ImportRunStatus.Succeeded);
    }

    // =====================================================================================
    // US5 — clientes sob demanda
    // =====================================================================================

    [Fact]
    public async Task Run_NewCpf_CreatesProtectedCustomer()
    {
        var (token, _) = await _fixture.NewSellerAsync();

        var report = await RunAsync(token, TestDataHelper.SCENARIO_OK, start: End, end: End);

        report.Counts.CustomersCreated.Should().Be(2);

        var list = await _fixture.GetAsync("/customers?page=1&pageSize=50", token);
        var customers = list.As<PagedResult<CustomerInfo>>().Items;

        customers.Should().HaveCount(2).And.OnlyContain(c => c.CpfMasked.StartsWith("***."));
        FullCpf.IsMatch(list.Body).Should().BeFalse("a listagem só mostra a máscara");
    }

    [Fact]
    public async Task Run_SameCpfAcrossBlocks_SingleCustomer()
    {
        var (token, _) = await _fixture.NewSellerAsync();

        await RunAsync(token, TestDataHelper.SCENARIO_OK, end: new DateOnly(2026, 10, 9));

        var orders = (await ListAllOrdersAsync(token))
            .Where(o => (o.OrderNumber == "41900001") || (o.OrderNumber == "41900004"))
            .ToList();

        orders.Should().HaveCount(3);
        orders.Select(o => o.CustomerId).Distinct().Should().ContainSingle();
    }

    [Fact]
    public async Task Reimport_V2_UpdatesNameAndKeepsCustomer()
    {
        var (token, _) = await _fixture.NewSellerAsync();

        await RunAsync(token, TestDataHelper.SCENARIO_OK, end: new DateOnly(2026, 10, 2));
        var order = (await ListAllOrdersAsync(token)).Single(o => o.OrderNumber == "41900001");
        var before = (await _fixture.GetAsync($"/customers/{order.CustomerId}", token)).As<CustomerDetailInfo>();

        await RunAsync(token, TestDataHelper.SCENARIO_OK, password: "v2", end: new DateOnly(2026, 10, 2));

        var after = (await _fixture.GetAsync($"/customers/{order.CustomerId}", token)).As<CustomerDetailInfo>();

        after.Name.Should().Be("Ana Beatriz S. Mello");
        after.Id.Should().Be(before.Id);
        after.CpfMasked.Should().Be(before.CpfMasked);
        after.CreatedAt.Should().Be(before.CreatedAt);
    }

    [Fact]
    public async Task Run_InvalidCpf_ReportsErrorAndContinues()
    {
        var (token, _) = await _fixture.NewSellerAsync();

        var report = await RunAsync(token, "cpf-invalido");

        report.Status.Should().Be(ImportRunStatus.Partial);
        report.Counts.Failed.Should().Be(2);
        report.Changes.Where(c => c.Action == ImportChangeAction.Error)
            .Should().HaveCount(2)
            .And.OnlyContain(c => c.ErrorMessage == "CPF ausente ou inválido na origem." && c.OrderId == null);
        (await _fixture.CountOrdersAsync(token)).Should().Be(TestDataHelper.IMPORT_ORDER_COUNT - 2);
    }

    [Fact]
    public async Task Responses_NeverContainFullCpf()
    {
        var (token, _) = await _fixture.NewSellerAsync();

        var report = await RunAsync(token, TestDataHelper.SCENARIO_OK, start: End, end: End);

        foreach (var path in new[] { $"{RUNS}/{report.Id}", $"{RUNS}?page=1&pageSize=50", "/orders?page=1&pageSize=50" })
        {
            var body = (await _fixture.GetAsync(path, token)).Body;

            FullCpf.IsMatch(body).Should().BeFalse($"{path} não pode trazer CPF completo");
        }
    }

    // =====================================================================================
    // US6 — fotos
    // =====================================================================================

    [Fact]
    public async Task Run_RepeatedSku_StoresSingleImage()
    {
        var (token, _) = await _fixture.NewSellerAsync();

        var report = await RunAsync(token, TestDataHelper.SCENARIO_OK, end: new DateOnly(2026, 10, 2));

        report.Counts.ImagesStored.Should().Be(4);

        var withSku0 = new List<long?>();

        foreach (var order in await ListAllOrdersAsync(token))
        {
            var detail = await GetOrderAsync(token, order.Id);

            withSku0.AddRange(detail.Items.Where(i => i.SkuRef == "42CA500UIE_12182").Select(i => i.ImageId));
        }

        withSku0.Should().HaveCountGreaterThanOrEqualTo(3);
        withSku0.Should().OnlyContain(id => id != null);
        withSku0.Distinct().Should().ContainSingle();

        var image = await _fixture.GetAsync($"/images/{withSku0[0]}", token);
        image.StatusCode.Should().Be(200, image.ToString());

        var info = image.As<Ambrosia.DTO.Images.ImageInfo>();
        info.Source.Should().Be(ImageSource.Bot);
        (await StorageHttp.GetAsync(info.Url!)).StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }

    [Fact]
    public async Task Run_PhotoError_SavesOrderWithoutImage()
    {
        var (token, _) = await _fixture.NewSellerAsync();

        var report = await RunAsync(token, "foto-erro", start: End, end: End);

        report.Status.Should().Be(ImportRunStatus.Partial);
        report.Counts.ImagesFailed.Should().BeGreaterThan(0);
        report.Counts.Inserted.Should().Be(3);

        var orders = await ListAllOrdersAsync(token);
        var items = new List<OrderItemInfo>();

        foreach (var order in orders)
        {
            items.AddRange((await GetOrderAsync(token, order.Id)).Items);
        }

        items.Where(i => i.SkuRef == "42CA500UIE_12182").Should().OnlyContain(i => i.ImageId == null);
    }

    [Fact]
    public async Task Run_GifPhoto_IsRejectedAndReported()
    {
        var (token, _) = await _fixture.NewSellerAsync();

        var report = await RunAsync(token, "foto-gif", start: End, end: End);

        report.Status.Should().Be(ImportRunStatus.Partial);
        report.Counts.ImagesFailed.Should().BeGreaterThan(0);
        report.Counts.Inserted.Should().Be(3);
    }

    [Fact]
    public async Task Reimport_SameUrl_DoesNotStoreAgain()
    {
        var (token, _) = await _fixture.NewSellerAsync();

        await RunAsync(token, TestDataHelper.SCENARIO_OK, start: End, end: End);
        var again = await RunAsync(token, TestDataHelper.SCENARIO_OK, start: End, end: End);

        again.Counts.ImagesStored.Should().Be(0);
        again.Counts.ImagesFailed.Should().Be(0);
    }

    [Fact]
    public async Task Reimport_AfterPhotoError_RetriesDownload()
    {
        var (token, _) = await _fixture.NewSellerAsync();

        await RunAsync(token, "foto-erro", start: End, end: End);
        var retry = await RunAsync(token, TestDataHelper.SCENARIO_OK, start: End, end: End);

        retry.Counts.ImagesStored.Should().BeGreaterThan(0);
        retry.Counts.ImagesFailed.Should().Be(0);
    }

    [Fact]
    public async Task RepeatBlock_AfterOrderFailure_ReusesStoredImage()
    {
        var (token, _) = await _fixture.NewSellerAsync();

        await RunAsync(token, "falha-bloco3");
        var repeat = await RunAsync(token, TestDataHelper.SCENARIO_OK, start: Block3Start, end: Block3End);

        repeat.Status.Should().Be(ImportRunStatus.Succeeded);

        // A foto do 15º pedido (SKU 3) já foi gravada antes da falha: a repetição só liga.
        repeat.Counts.ImagesStored.Should().Be(0);

        var failed = (await ListAllOrdersAsync(token)).Single(o => o.OrderNumber == "41900030");
        (await GetOrderAsync(token, failed.Id)).Items.Should().OnlyContain(i => i.ImageId != null);
    }

    // =====================================================================================
    // US7 — acompanhar, cancelar, histórico
    // =====================================================================================

    [Fact]
    public async Task Start_WhileActive_Returns409()
    {
        var (token, _) = await _fixture.NewSellerAsync();

        var first = await _fixture.StartImportAsync(token, Login("ok"), "v1", Start, End);
        var second = await _fixture.StartImportRawAsync(token, Login("ok"), "v1", Start, End);

        second.StatusCode.Should().Be(409, second.ToString());
        second.As<ErrorResult>().Mensagem.Should().Be("Já existe importação em andamento.");

        (await _fixture.WaitUntilFinishedAsync(first.Id, token)).Status.Should().Be(ImportRunStatus.Succeeded);
    }

    [Fact]
    public async Task Current_ReturnsActiveRunThen204()
    {
        var (token, _) = await _fixture.NewSellerAsync();

        var run = await _fixture.StartImportAsync(token, Login("ok"), "v1", Start, End);

        var current = await _fixture.GetAsync($"{RUNS}/current", token);
        current.StatusCode.Should().BeOneOf(200, 204);

        if (current.StatusCode == 200)
        {
            current.As<ImportRunDetailInfo>().Id.Should().Be(run.Id);
        }

        await _fixture.WaitUntilFinishedAsync(run.Id, token);

        (await _fixture.GetAsync($"{RUNS}/current", token)).StatusCode.Should().Be(204);
    }

    [Fact]
    public async Task Cancel_Running_EndsAfterCurrentBlock()
    {
        var (token, _) = await _fixture.NewSellerAsync();

        var run = await _fixture.StartImportAsync(token, Login("ok"), "v1", Start, End);
        await _fixture.WaitUntilBlocksProgressAsync(run.Id, token, minBlocksFinished: 1);

        var cancel = await _fixture.PostAsync($"{RUNS}/{run.Id}/cancel", token, new { });

        if (cancel.StatusCode == 409)
        {
            return; // O fake terminou antes do pedido: nada a cancelar (coberto por Cancel_Finished).
        }

        cancel.StatusCode.Should().Be(202, cancel.ToString());

        var final = await _fixture.WaitUntilFinishedAsync(run.Id, token);

        final.Status.Should().BeOneOf(ImportRunStatus.Cancelled, ImportRunStatus.Succeeded);

        if (final.Status == ImportRunStatus.Cancelled)
        {
            final.Blocks.Should().Contain(b => b.Status == ImportBlockStatus.Succeeded);
            final.Blocks.Should().NotContain(b => b.Status == ImportBlockStatus.Running || b.Status == ImportBlockStatus.Pending);
        }
    }

    [Fact]
    public async Task Cancel_Finished_Returns409()
    {
        var (token, _) = await _fixture.NewSellerAsync();

        var report = await RunAsync(token, TestDataHelper.SCENARIO_OK, start: End, end: End, dryRun: true);
        var cancel = await _fixture.PostAsync($"{RUNS}/{report.Id}/cancel", token, new { });

        cancel.StatusCode.Should().Be(409, cancel.ToString());
        cancel.As<ErrorResult>().Mensagem.Should().Be("A execução já foi finalizada.");
    }

    [Fact]
    public async Task List_ReturnsPagedDescending()
    {
        var (token, _) = await _fixture.NewSellerAsync();

        var first = await RunAsync(token, TestDataHelper.SCENARIO_OK, start: End, end: End, dryRun: true);
        var second = await RunAsync(token, TestDataHelper.SCENARIO_OK, start: End, end: End, dryRun: true);

        var page = (await _fixture.GetAsync($"{RUNS}?page=1&pageSize=1", token)).As<PagedResult<ImportRunInfo>>();

        page.TotalCount.Should().Be(2);
        page.Items.Should().ContainSingle().Which.Id.Should().Be(second.Id);

        var page2 = (await _fixture.GetAsync($"{RUNS}?page=2&pageSize=1", token)).As<PagedResult<ImportRunInfo>>();
        page2.Items.Should().ContainSingle().Which.Id.Should().Be(first.Id);
    }

    [Fact]
    public async Task List_PageSizeAbove50_Returns400()
    {
        var (token, _) = await _fixture.NewSellerAsync();

        var response = await _fixture.GetAsync($"{RUNS}?page=1&pageSize=51", token);

        response.StatusCode.Should().Be(400, response.ToString());
        response.As<ErrorResult>().Erros.Should().Contain(e => e.Campo == "pageSize");
    }

    [Fact]
    public async Task Report_CountDrop_SetsWarning()
    {
        var (token, _) = await _fixture.NewSellerAsync();

        await RunAsync(token, TestDataHelper.SCENARIO_OK);
        var empty = await RunAsync(token, "vazio");

        empty.CountDropWarning.Should().BeTrue();
        empty.PreviousRunOrdersCount.Should().Be(TestDataHelper.IMPORT_ORDER_COUNT);
    }
}
