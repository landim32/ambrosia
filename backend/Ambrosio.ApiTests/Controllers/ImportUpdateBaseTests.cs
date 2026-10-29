using Ambrosio.ApiTests.Fixtures;
using Ambrosio.ApiTests.Helpers;
using Ambrosio.DTO.Common;
using Ambrosio.DTO.Enums;
using Ambrosio.DTO.Imports;
using FluentAssertions;

namespace Ambrosio.ApiTests.Controllers;

/// <summary>
/// "Atualizar base" (feature 008, US3 — T026). Rotas <c>GET /imports/mais-vendas/defaults</c> (o bloco
/// <c>updateBase</c>) e <c>POST /imports/mais-vendas/runs/update-base</c>.
/// <para>
/// O ponto da história é que <b>a vendedora não escolhe data nenhuma</b> (FR-013): o corpo do POST é só
/// <c>dryRun</c>, e o período — do último pedido importado, inclusive, até hoje em São Paulo — é
/// calculado pelo servidor, que é também quem responde pelo motivo quando ele não existe (FR-015). Por
/// isso cada teste compara o <c>POST</c> com o que os <c>defaults</c> acabaram de mostrar: a tela lê um,
/// o botão manda o outro, e os dois têm de dizer o mesmo (SC-005).
/// </para>
/// <para>
/// Vendedora nova por teste: <c>GetSuggestedStartDateAsync</c> é o <c>MAX(order_date)</c> dos pedidos
/// vindos do +Vendas, então compartilhar a linha entre testes faria um herdar a base do outro.
/// </para>
/// </summary>
[Collection(ApiTestCollection.API_TESTS)]
public class ImportUpdateBaseTests
{
    private const string RUNS = "/imports/mais-vendas/runs";
    private const string DEFAULTS = "/imports/mais-vendas/defaults";

    /// <summary>Motivo 1 de indisponibilidade (FR-015): nada importado — nem pedido, nem simulação.</summary>
    private const string NOTHING_REASON = "Ainda não há pedidos importados para atualizar.";

    /// <summary>
    /// Frase única de "sem credenciais" (FR-002/FR-004 da 008), no campo <c>login</c>.
    /// </summary>
    private const string NO_CREDENTIALS_MESSAGE =
        "Cadastre as credenciais do +Vendas na configuração.";

    private static readonly DateOnly Start = TestDataHelper.ImportStart;
    private static readonly DateOnly End = TestDataHelper.ImportEnd;

    private readonly ApiTestFixture _fixture;

    public ImportUpdateBaseTests(ApiTestFixture fixture) => _fixture = fixture;

    /// <summary>
    /// Segunda frase possível de <c>updateBase.reason</c> (FR-015), montada com o meses configurado — a
    /// suíte confere o número do ambiente, como faz com a recusa de período (research R6).
    /// </summary>
    private string TooLongReason =>
        $"A última importação foi há mais de {_fixture.MaisVendasMaxRangeMonths} meses. " +
        $"Importe em partes de até {_fixture.MaisVendasMaxRangeMonths} meses, escolhendo as datas.";

    // =====================================================================================
    // indisponível
    // =====================================================================================

    [Fact]
    public async Task UpdateBase_WithoutImportedOrders_Returns400WithReason_AndCreatesNoRun()
    {
        // T026 / FR-015: vendedora nova não tem o que atualizar. O `POST` devolve exatamente o motivo que
        // os defaults já mostravam, no campo `startDate` (a data que falta), e nenhuma execução nasce.
        var (token, _) = await _fixture.NewSellerAsync();

        await _fixture.SaveCredentialsAsync(token, Login(TestDataHelper.SCENARIO_OK), "v1");

        var defaults = (await _fixture.GetAsync(DEFAULTS, token)).As<ImportDefaultsInfo>();

        defaults.UpdateBase.Available.Should().BeFalse();
        defaults.UpdateBase.Reason.Should().Be(NOTHING_REASON);
        defaults.UpdateBase.StartDate.Should().BeNull();
        defaults.UpdateBase.EndDate.Should().BeNull();

        var response = await _fixture.StartUpdateBaseRawAsync(token);

        response.StatusCode.Should().Be(400, response.ToString());
        response.As<ErrorResult>().Erros.Should().ContainSingle(e =>
            e.Campo == "startDate" && e.Mensagem == defaults.UpdateBase.Reason);

        // O corpo da recusa é uma frase só: nem período inventado, nem credencial (FR-003/FR-006).
        response.Body.Should().NotContain(TestDataHelper.TRACEABLE_PASSWORD);
        response.Body.Should().NotContain("s482");
        (await _fixture.GetAsync($"{RUNS}?page=1&pageSize=10", token))
            .As<PagedResult<ImportRunInfo>>().TotalCount.Should().Be(0);

        // As duas frases possíveis do contrato (FR-015) são estas — nada além.
        defaults.UpdateBase.Reason.Should().BeOneOf(NOTHING_REASON, TooLongReason);
    }

    [Fact]
    public async Task UpdateBase_AfterDryRunOnlyImport_StillHasNothingToUpdate()
    {
        // FR-011: simulação não atualizou base nenhuma — os contadores do relatório existem, mas nenhum
        // pedido foi gravado, então o botão continua desligado com o mesmo motivo (T026).
        var (token, _) = await _fixture.NewSellerAsync();

        var simulated = await _fixture.StartImportAsync(
            token, Login(TestDataHelper.SCENARIO_OK), "v1", End, End, dryRun: true);

        var report = await _fixture.WaitUntilFinishedAsync(simulated.Id, token);

        report.Status.Should().Be(ImportRunStatus.Succeeded, report.ErrorMessage);
        report.Counts.Inserted.Should().BeGreaterThan(0, "a simulação viu pedidos, só não os gravou");

        var defaults = (await _fixture.GetAsync(DEFAULTS, token)).As<ImportDefaultsInfo>();

        defaults.SuggestedStartDate.Should().BeNull();
        defaults.UpdateBase.Available.Should().BeFalse();
        defaults.UpdateBase.Reason.Should().Be(NOTHING_REASON);

        (await _fixture.StartUpdateBaseRawAsync(token)).StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task UpdateBase_WithoutCredentials_Returns400OnLoginField_AndCreatesNoRun()
    {
        // A checagem de credenciais vem antes do cálculo do período (ImportRunService.UpdateBaseAsync):
        // sem cadastro não há o que autenticar na origem, e a recusa é a mesma frase do início por
        // período (FR-002/FR-004 da 008), mesmo que a vendedora já tenha pedidos importados.
        var (token, _) = await _fixture.NewSellerAsync();

        var response = await _fixture.StartUpdateBaseRawAsync(token);

        response.StatusCode.Should().Be(400, response.ToString());
        response.As<ErrorResult>().Erros.Should().ContainSingle(e =>
            e.Campo == "login" && e.Mensagem == NO_CREDENTIALS_MESSAGE);

        (await _fixture.GetAsync($"{RUNS}?page=1&pageSize=10", token))
            .As<PagedResult<ImportRunInfo>>().TotalCount.Should().Be(0);
    }

    // =====================================================================================
    // disponível
    // =====================================================================================

    [Fact]
    public async Task UpdateBase_AfterFakeImport_StartsWithTheServerPeriodAndKindUpdateBase()
    {
        // T026 / FR-011/FR-012/FR-013. O período vem do servidor: do último pedido importado (inclusive)
        // até hoje em São Paulo. Como o Fake só entrega pedidos entre 26/09 e 25/10/2026, a janela do
        // "mais de 3 meses" ainda não alcança a base — enquanto a suíte rodar nesse trimestre, `available`
        // é verdadeiro; se os seeds do Fake mudarem de época, é este teste que avisa primeiro.
        var (token, _) = await _fixture.NewSellerAsync();

        await RunOkAsync(token);

        var defaults = (await _fixture.GetAsync(DEFAULTS, token)).As<ImportDefaultsInfo>();

        defaults.UpdateBase.Available.Should().BeTrue(defaults.UpdateBase.Reason);
        defaults.UpdateBase.StartDate.Should().Be(End);
        defaults.UpdateBase.EndDate.Should().Be(defaults.SuggestedEndDate);
        defaults.UpdateBase.Reason.Should().BeNull();

        var run = await _fixture.StartUpdateBaseAsync(token);

        run.Kind.Should().Be(ImportRunKind.UpdateBase);
        run.StartedVia.Should().Be(ImportRunStartedVia.Screen);
        run.Mode.Should().Be(ImportRunMode.Apply);
        run.StartDate.Should().Be(defaults.UpdateBase.StartDate);
        run.EndDate.Should().Be(defaults.UpdateBase.EndDate);

        var report = await _fixture.WaitUntilFinishedAsync(run.Id, token);

        report.Kind.Should().Be(ImportRunKind.UpdateBase);
        report.Status.Should().Be(ImportRunStatus.Succeeded, report.ErrorMessage);

        // Continuação, não recomeço: os pedidos do último dia já estão gravados, então a base repete
        // (unchanged) em vez de duplicar (SC-002 da 006, FR-011 da 008).
        report.Counts.Inserted.Should().Be(0);
        report.Counts.Unchanged.Should().BeGreaterThan(0);
        (await _fixture.CountOrdersAsync(token)).Should().Be(TestDataHelper.IMPORT_ORDER_COUNT);
    }

    [Fact]
    public async Task UpdateBase_DryRun_StartsWithoutWritingAnything()
    {
        // FR-032 da 006 vale para o "Atualizar base" também: o corpo leva só `dryRun`, e a execução nasce
        // em modo simulação, sem gravar pedido nenhum (T026).
        var (token, _) = await _fixture.NewSellerAsync();

        await RunOkAsync(token);
        var before = await _fixture.CountOrdersAsync(token);

        var run = await _fixture.StartUpdateBaseAsync(token, dryRun: true);

        run.Mode.Should().Be(ImportRunMode.DryRun);
        run.Kind.Should().Be(ImportRunKind.UpdateBase);

        var report = await _fixture.WaitUntilFinishedAsync(run.Id, token);

        report.Status.Should().Be(ImportRunStatus.Succeeded, report.ErrorMessage);
        (await _fixture.CountOrdersAsync(token)).Should().Be(before);
    }

    [Fact]
    public async Task UpdateBase_WhileAnotherRunIsActive_Returns409()
    {
        // FR-016 da 006, agora também pelo caminho do servidor: uma execução por vendedora, tenha ela
        // vindo do período escolhido ou do "Atualizar base" (índice único parcial).
        //
        // A base precisa estar disponível ANTES de a segunda execução começar: `UpdateBaseAsync` confere
        // bloqueio, credenciais e disponibilidade e só então grava — com a vendedora sem nada importado, a
        // recusa seria o 400 de "ainda não há pedidos", não o 409 que este caso procura.
        var (token, _) = await _fixture.NewSellerAsync();

        await RunOkAsync(token);

        var active = await _fixture.StartImportAsync(
            token, Login(TestDataHelper.SCENARIO_OK), "v1", Start, End, dryRun: true);

        var response = await _fixture.StartUpdateBaseRawAsync(token);

        response.StatusCode.Should().Be(409, response.ToString());
        response.As<ErrorResult>().Mensagem.Should().Be("Já existe importação em andamento.");

        // A recusa não criou nada: continuam as duas execuções desta vendedora (a do cenário + a ativa).
        (await _fixture.GetAsync($"{RUNS}?page=1&pageSize=10", token))
            .As<PagedResult<ImportRunInfo>>().TotalCount.Should().Be(2);

        (await _fixture.WaitUntilFinishedAsync(active.Id, token)).Status.Should()
            .BeOneOf(ImportRunStatus.Succeeded, ImportRunStatus.Partial);
    }

    [Fact]
    public async Task UpdateBase_WhileLocked_Returns429WithRetryAfter()
    {
        // O bloqueio por recusas (FR-008a da 006) é conferido antes do cálculo do período, e o
        // "Atualizar base" não é uma porta lateral: responde 429 com Retry-After como o início normal
        // (contrato api.md: "bloqueio (423)" na 006, que a implementação devolve como 429 — mesma regra
        // do `POST /runs`, conferida pelo teste de período).
        var (token, _) = await _fixture.NewSellerAsync();

        for (var i = 0; i < _fixture.MaisVendasMaxFailedLogins; i++)
        {
            // Uma execução por vez: cada recusa tem de terminar antes da próxima tentativa, senão o que o
            // teste mediria seria o 409 de execução ativa, não o bloqueio.
            var refused = await _fixture.StartImportAsync(
                token, Login(TestDataHelper.SCENARIO_OK), "recusada", End, End, dryRun: true);

            (await _fixture.WaitUntilFinishedAsync(refused.Id, token))
                .Status.Should().Be(ImportRunStatus.Failed);
        }

        var blocked = await _fixture.StartUpdateBaseRawAsync(token);

        blocked.StatusCode.Should().Be(429, blocked.ToString());
        blocked.As<ErrorResult>().Mensagem.Should().StartWith("Muitas tentativas com credenciais recusadas.");
        blocked.Header("Retry-After").Should().NotBeNullOrEmpty();

        (await _fixture.GetAsync(DEFAULTS, token)).As<ImportDefaultsInfo>().LockedUntil.Should().NotBeNull();
    }

    // =====================================================================================
    // Helpers de cenário
    // =====================================================================================

    /// <summary>Cadastra o cenário bom e roda o período de referência em gravação até o fim.</summary>
    private async Task RunOkAsync(string token)
    {
        var run = await _fixture.StartImportAsync(token, Login(TestDataHelper.SCENARIO_OK), "v1", Start, End);

        var report = await _fixture.WaitUntilFinishedAsync(run.Id, token);

        report.Status.Should().Be(ImportRunStatus.Succeeded, report.ErrorMessage);
        report.Counts.Inserted.Should().Be(TestDataHelper.IMPORT_ORDER_COUNT);
    }

    private static string Login(string scenario, string? account = null) =>
        TestDataHelper.FakeLogin(scenario, account);
}
