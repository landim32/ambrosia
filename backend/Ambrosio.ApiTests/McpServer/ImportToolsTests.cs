using System.Globalization;
using System.Text.Json;
using Ambrosio.ApiTests.Fixtures;
using Ambrosio.ApiTests.Helpers;
using Ambrosio.DTO.Common;
using Ambrosio.DTO.Enums;
using Ambrosio.DTO.Imports;
using FluentAssertions;
using Flurl;
using Flurl.Http;

namespace Ambrosio.ApiTests.McpServer;

/// <summary>
/// T032 [US4]: importação do +Vendas pelo plugin (feature 008, research R9/R10,
/// contracts/mcp-tools.md). As ferramentas agora <b>iniciam de verdade</b> — com as credenciais
/// guardadas da vendedora, que nunca passam pela conversa — e <c>iniciar_importacao</c> devolve o número
/// da execução que a API enxerga com <c>startedVia: mcp</c>.
/// <para>
/// Cada cenário cadastra credenciais Fake pelo <c>PUT /imports/mais-vendas/credentials</c> da API antes
/// de chamar a ferramenta: o worker escolhe o cenário do coletor pela linha gravada (FR-001/FR-003 da
/// 008), então sem cadastro a única resposta possível é o erro com o link da tela — e o "sem
/// credenciais" é justamente o caso que não pode cadastrar nada.
/// </para>
/// <para>
/// Período de um dia nos cenários que executam: o Fake atrasa cada pedido (research §16), e a suíte não
/// precisa de 49 pedidos para provar quem iniciou a execução.
/// </para>
/// </summary>
[Collection(ApiTestCollection.API_TESTS)]
public class ImportToolsTests(ApiTestFixture fixture)
{
    /// <summary>Escopos das ferramentas de importação (leitura + escrita), usados também por T036.</summary>
    public const string ImportScopes = "openid email profile imports:read imports:write";

    /// <summary>Frase única do plugin quando faltam credenciais (contracts/mcp-tools.md).</summary>
    private const string NoCredentialsMessage =
        "Cadastre as credenciais do +Vendas na configuração do Ambrosio:";

    /// <summary>Rota da tela de configuração, montada sobre <c>Mcp:FrontendBaseUrl</c> (FR-018).</summary>
    private const string SettingsPath = "/configuracoes/mais-vendas";

    /// <summary>Aviso padrão de prévia do <c>WriteToolHelper</c> (R11 da 007).</summary>
    private const string PreviewNotice = "Nada foi gravado. Confirme para gravar.";

    /// <summary>Período de referência do quickstart (5 blocos) — usado nas recusas de período.</summary>
    private const string FullPeriodStart = "2026-09-26";
    private const string FullPeriodEnd = "2026-10-25";

    /// <summary>Dia único (bloco único) dos cenários que executam de verdade.</summary>
    private static readonly string SingleDay =
        TestDataHelper.ImportEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Ferramentas de importação do contrato da 008, incluindo a nova <c>atualizar_base</c>.</summary>
    private static readonly string[] ImportToolNames =
    [
        "sugestao_periodo_importacao", "iniciar_importacao", "atualizar_base",
        "importacao_atual", "obter_importacao", "listar_importacoes", "cancelar_importacao"
    ];

    /// <summary>Chaves que nenhum inputSchema pode ter: credencial não é argumento (FR-018).</summary>
    private static readonly string[] CredentialKeys = ["login", "senha", "password", "credencial", "credenciais"];

    // =====================================================================================
    // sugestão de período
    // =====================================================================================

    [Fact]
    public async Task SugestaoPeriodo_ShouldMatchTheApiDefaults()
    {
        var seller = await McpSeller.CreateAsync(fixture, "Ana Periodo MCP", ImportScopes);

        await fixture.SaveCredentialsAsync(seller.User.ApiToken, Login(TestDataHelper.SCENARIO_OK), "v1");

        var tool = WriteToolsTests.Structured(await seller.CallAsync("sugestao_periodo_importacao"));
        using var rest = await seller.RestGetAsync("/imports/mais-vendas/defaults");

        var defaults = ApiTestFixture.Deserialize<ImportDefaultsInfo>(rest.RootElement.GetRawText());

        tool.GetProperty("fimSugerido").GetString()
            .Should().Be(defaults.SuggestedEndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        tool.GetProperty("diasPorBloco").GetInt32().Should().Be(defaults.BlockDays);

        // T032: saiu `maxDias`, entrou `maxMeses`, e o número é o mesmo que a API devolve em
        // `maxRangeMonths` — conversa e tela têm de repetir a mesma regra (research R6, SC-005).
        tool.GetProperty("maxMeses").GetInt32().Should().Be(defaults.MaxRangeMonths);
        tool.TryGetProperty("maxDias", out _).Should().BeFalse("o limite passou a ser em meses (T023)");

        tool.GetProperty("temCredenciais").GetBoolean().Should().Be(defaults.HasCredentials).And.BeTrue();
        tool.GetProperty("situacaoCredenciais").GetString().Should().Be("valid");

        // O "sem dados ainda" some do JSON (WhenWritingNull): sem pedido importado não há início sugerido,
        // e é assim que a conversa sabe que precisa perguntar a data.
        tool.TryGetProperty("inicioSugerido", out _).Should().BeFalse();
        tool.TryGetProperty("bloqueadoAte", out _).Should().BeFalse("esta vendedora não está bloqueada");

        var updateBase = tool.GetProperty("atualizarBase");

        updateBase.GetProperty("disponivel").GetBoolean().Should().Be(defaults.UpdateBase.Available).And.BeFalse();
        updateBase.GetProperty("motivo").GetString().Should().Be(defaults.UpdateBase.Reason);
    }

    [Fact]
    public async Task SugestaoPeriodo_WithoutCredentials_StillAnswersWithTheState()
    {
        // A ferramenta de leitura não exige credencial: é justamente ela que a conversa chama para
        // descobrir que falta cadastrá-las (temCredenciais false, situação "none") — sem virar erro.
        var seller = await McpSeller.CreateAsync(fixture, "Bia Sem Credencial", ImportScopes);

        var result = await seller.CallAsync("sugestao_periodo_importacao");

        result.IsToolError.Should().BeFalse(result.Text);

        var tool = WriteToolsTests.Structured(result);

        tool.GetProperty("temCredenciais").GetBoolean().Should().BeFalse();
        tool.GetProperty("situacaoCredenciais").GetString().Should().Be("none");
        tool.GetProperty("atualizarBase").GetProperty("disponivel").GetBoolean().Should().BeFalse();
    }

    // =====================================================================================
    // iniciar_importacao
    // =====================================================================================

    [Fact]
    public async Task IniciarImportacao_PreviewCreatesNoRun()
    {
        var seller = await McpSeller.CreateAsync(fixture, "Ana Previa MCP", ImportScopes);

        await fixture.SaveCredentialsAsync(seller.User.ApiToken, Login(TestDataHelper.SCENARIO_OK), "v1");

        var preview = WriteToolsTests.Structured(
            await seller.CallAsync("iniciar_importacao", new { inicio = FullPeriodStart, fim = FullPeriodEnd }));

        preview.GetProperty("gravado").GetBoolean().Should().BeFalse();
        preview.GetProperty("aviso").GetString().Should().Be(PreviewNotice);
        preview.GetProperty("previa").GetProperty("inicio").GetString().Should().Be(FullPeriodStart);
        preview.GetProperty("previa").GetProperty("fim").GetString().Should().Be(FullPeriodEnd);
        preview.GetProperty("previa").GetProperty("simular").GetBoolean().Should().BeFalse();

        // FR-017 da 008 (a prévia valida sem criar): o histórico da vendedora continua vazio.
        (await CountRunsAsync(seller)).Should().Be(0);
    }

    [Fact]
    public async Task IniciarImportacao_Confirm_CreatesRunTheApiSeesAsStartedByMcp()
    {
        var seller = await McpSeller.CreateAsync(fixture, "Ana Inicia MCP", ImportScopes);

        await fixture.SaveCredentialsAsync(seller.User.ApiToken, Login(TestDataHelper.SCENARIO_OK), "v1");

        var started = WriteToolsTests.Structured(await seller.CallAsync(
            "iniciar_importacao", new { inicio = SingleDay, fim = SingleDay, confirmar = true }));

        started.GetProperty("gravado").GetBoolean().Should().BeTrue();

        var runId = started.GetProperty("registro").GetProperty("importacao_id").GetInt64();

        runId.Should().BeGreaterThan(0, "a ferramenta devolve o número da execução criada (R10 da 008)");
        started.GetProperty("registro").GetProperty("blocosTotal").GetInt32().Should().Be(1);
        started.GetProperty("registro").GetProperty("simular").GetBoolean().Should().BeFalse();

        using var rest = await seller.RestGetAsync($"/imports/mais-vendas/runs/{runId}");

        var run = ApiTestFixture.Deserialize<ImportRunDetailInfo>(rest.RootElement.GetRawText());

        run.Id.Should().Be(runId);
        run.StartDate.Should().Be(TestDataHelper.ImportEnd);
        run.StartedVia.Should().Be(ImportRunStartedVia.Mcp);
        run.Kind.Should().Be(ImportRunKind.Period);

        // A execução rodou de fato com a credencial guardada: sem cadastro não haveria login a decifrar,
        // e o worker teria encerrado em "Credenciais do +Vendas não cadastradas.".
        var finished = await fixture.WaitUntilFinishedAsync(runId, seller.User.ApiToken);

        finished.Status.Should().Be(ImportRunStatus.Succeeded, finished.ErrorMessage);
        finished.Counts.Inserted.Should().Be(3, "25/10 tem três registros no Fake");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IniciarImportacao_WithoutCredentials_IsToolErrorWithTheSettingsLink(bool confirmar)
    {
        // FR-004/FR-018 da 008: sem cadastro não há o que coletar, e a recusa do plugin é a frase com o
        // link da tela — a única superfície onde a vendedora cadastra (nenhuma ferramenta toca em
        // credencial). Vale igual para a prévia e para a confirmação.
        var seller = await McpSeller.CreateAsync(fixture, "Bia Link MCP", ImportScopes);

        var result = await seller.CallAsync(
            "iniciar_importacao", new { inicio = FullPeriodStart, fim = FullPeriodEnd, confirmar });

        result.IsToolError.Should().BeTrue(result.Text);
        result.Text.Should().Contain(NoCredentialsMessage);
        result.Text.Should().Contain(SettingsPath);
        result.RawBody.Should().NotContain("password");

        // E nenhuma das duas criou execução.
        (await CountRunsAsync(seller)).Should().Be(0);
    }

    [Fact]
    public async Task IniciarImportacao_AboveTheMonthLimit_ReturnsTheApiMessage()
    {
        // SC-005: mesma regra, mesma frase. A API é a fonte do texto — o teste lê o `400` dela e compara
        // com o `isError` da ferramenta, em vez de duplicar a mensagem aqui.
        var seller = await McpSeller.CreateAsync(fixture, "Ana Limite MCP", ImportScopes);

        await fixture.SaveCredentialsAsync(seller.User.ApiToken, Login(TestDataHelper.SCENARIO_OK), "v1");

        // A fronteira do contrato (26/07 → 26/10 é o primeiro dia além de 3 meses): fixa e no passado, para
        // a recusa medida ser a do limite de meses e nunca a de "data final no futuro".
        var start = new DateOnly(2026, 7, 26);
        var tooLong = Iso(start.AddMonths(fixture.MaisVendasMaxRangeMonths));

        var api = await fixture.PostAsync(
            "/imports/mais-vendas/runs",
            seller.User.ApiToken,
            new { startDate = Iso(start), endDate = tooLong, dryRun = false });

        api.StatusCode.Should().Be(400, api.ToString());
        var apiMessage = api.As<ErrorResult>().Erros[0].Mensagem;

        apiMessage.Should().Be($"O período não pode passar de {fixture.MaisVendasMaxRangeMonths} meses.");

        foreach (var confirmar in new[] { false, true })
        {
            var result = await seller.CallAsync(
                "iniciar_importacao", new { inicio = Iso(start), fim = tooLong, confirmar });

            result.IsToolError.Should().BeTrue($"{confirmar}: {result.Text}");
            result.Text.Should().Contain(apiMessage);
        }

        // A prévia também só valida: nenhuma das duas chamadas criou execução.
        (await CountRunsAsync(seller)).Should().Be(0);
    }

    // =====================================================================================
    // atualizar_base (nova)
    // =====================================================================================

    [Fact]
    public async Task AtualizarBase_PreviewThenStart_RunsAsUpdateBaseFromTheApi()
    {
        // T032 / FR-011/FR-013: a ferramenta não pede datas — lê o período calculado do servidor na
        // prévia e o repete na execução, com `kind: updateBase` e `startedVia: mcp` no relatório da API.
        var seller = await McpSeller.CreateAsync(fixture, "Ana Base MCP", ImportScopes);

        await fixture.SaveCredentialsAsync(seller.User.ApiToken, Login(TestDataHelper.SCENARIO_OK), "v1");
        await ImportOneDayAsync(seller);

        var defaults = ApiTestFixture.Deserialize<ImportDefaultsInfo>(
            (await seller.RestGetAsync("/imports/mais-vendas/defaults")).RootElement.GetRawText());

        defaults.UpdateBase.Available.Should().BeTrue(defaults.UpdateBase.Reason);

        var preview = WriteToolsTests.Structured(await seller.CallAsync("atualizar_base"));

        preview.GetProperty("gravado").GetBoolean().Should().BeFalse();
        preview.GetProperty("previa").GetProperty("inicio").GetString().Should().Be(Iso(defaults.UpdateBase.StartDate!.Value));
        preview.GetProperty("previa").GetProperty("fim").GetString().Should().Be(Iso(defaults.UpdateBase.EndDate!.Value));

        (await CountRunsAsync(seller)).Should().Be(1, "a prévia não criou execução — só a importação do cenário");

        var started = WriteToolsTests.Structured(await seller.CallAsync("atualizar_base", new { confirmar = true }));

        started.GetProperty("gravado").GetBoolean().Should().BeTrue();

        var runId = started.GetProperty("registro").GetProperty("importacao_id").GetInt64();

        using var rest = await seller.RestGetAsync($"/imports/mais-vendas/runs/{runId}");

        var run = ApiTestFixture.Deserialize<ImportRunDetailInfo>(rest.RootElement.GetRawText());

        run.Kind.Should().Be(ImportRunKind.UpdateBase);
        run.StartedVia.Should().Be(ImportRunStartedVia.Mcp);
        run.StartDate.Should().Be(defaults.UpdateBase.StartDate);
        run.EndDate.Should().Be(defaults.UpdateBase.EndDate);

        var finished = await fixture.WaitUntilFinishedAsync(runId, seller.User.ApiToken);

        finished.Status.Should().Be(ImportRunStatus.Succeeded, finished.ErrorMessage);

        // Continuação da base, não recomeço: os pedidos daquele dia já estavam gravados.
        finished.Counts.Inserted.Should().Be(0);
        finished.Counts.Unchanged.Should().Be(3);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AtualizarBase_WithoutCredentials_IsToolErrorWithTheSettingsLink(bool confirmar)
    {
        var seller = await McpSeller.CreateAsync(fixture, "Bia Base Sem Credencial", ImportScopes);

        var result = await seller.CallAsync("atualizar_base", new { confirmar });

        result.IsToolError.Should().BeTrue(result.Text);
        result.Text.Should().StartWith("Cadastre as credenciais do +Vendas na");

        // Prévia e confirmação passam pelo MESMO guard da ferramenta (`RequireCredentialsAsync`), que
        // devolve a frase do plugin com o link da tela (contracts/mcp-tools.md): a conversa não tem
        // tela do Ambrosio aberta, então sem o link a vendedora não tem para onde ir. O serviço também
        // confere as credenciais — a guarda da ferramenta é o que acrescenta o link, não o que recusa.
        result.Text.Should().Contain(NoCredentialsMessage);
        result.Text.Should().EndWith(SettingsPath, "o link é `{Mcp:FrontendBaseUrl}/configuracoes/mais-vendas`");

        (await CountRunsAsync(seller)).Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AtualizarBase_WithoutImportedOrders_IsRefusedWithTheReason(bool confirmar)
    {
        // FR-015: sem nada importado não há base a atualizar, e a ferramenta devolve o MESMO motivo que os
        // defaults mostram — prévia e execução recusam igual, sem criar execução.
        var seller = await McpSeller.CreateAsync(fixture, "Ana Base Vazia", ImportScopes);

        await fixture.SaveCredentialsAsync(seller.User.ApiToken, Login(TestDataHelper.SCENARIO_OK), "v1");

        var reason = ApiTestFixture.Deserialize<ImportDefaultsInfo>(
                (await seller.RestGetAsync("/imports/mais-vendas/defaults")).RootElement.GetRawText())
            .UpdateBase.Reason;

        reason.Should().Be("Ainda não há pedidos importados para atualizar.");

        var result = await seller.CallAsync("atualizar_base", new { confirmar });

        result.IsToolError.Should().BeTrue(result.Text);
        result.Text.Should().Be(reason);

        (await CountRunsAsync(seller)).Should().Be(0);
    }

    // =====================================================================================
    // credenciais fora da conversa (FR-018/SC-003) e auditoria
    // =====================================================================================

    [Theory]
    [InlineData("iniciar_importacao", "senha")]
    [InlineData("iniciar_importacao", "password")]
    [InlineData("iniciar_importacao", "login")]
    [InlineData("atualizar_base", "senha")]
    [InlineData("atualizar_base", "login")]
    public async Task ImportTools_WithCredentialArguments_AreRejectedAndNotAudited(string tool, string field)
    {
        // O schema é fechado (additionalProperties: false), então o argumento estranho morre no filtro de
        // tools, antes da ferramenta e antes da auditoria: a senha nem chega a existir em log algum.
        var seller = await McpSeller.CreateAsync(fixture, "Ana Senha na Conversa", ImportScopes);

        await fixture.SaveCredentialsAsync(seller.User.ApiToken, Login(TestDataHelper.SCENARIO_OK), "v1");

        var args = new Dictionary<string, object>
        {
            [field] = "segredo-que-nao-pode-vazar",
            ["confirmar"] = true
        };

        if (tool == "iniciar_importacao")
        {
            args["inicio"] = FullPeriodStart;
            args["fim"] = FullPeriodEnd;
        }

        var result = await seller.CallAsync(tool, args);

        result.IsToolError.Should().BeTrue(result.Text);
        result.Text.Should().Contain(field);
        result.RawBody.Should().NotContain("segredo-que-nao-pode-vazar", "a recusa nomeia a chave, nunca o valor");

        (await AuditAsync(seller, tool)).Should().BeEmpty($"{tool}: chamada recusada pelo schema não é auditada");
    }

    [Fact]
    public async Task ImportTools_SchemasAndDescriptions_NeverExposeCredentials()
    {
        // Além de recusar o argumento, a superfície pública tem de dizer isso: nenhum inputSchema tem chave
        // de credencial e a descrição das ferramentas de escrita orienta onde ela se cadastra (FR-018).
        var seller = await McpSeller.CreateAsync(fixture, "Ana Schema MCP", ImportScopes);

        var tools = (await seller.Mcp.ListToolsAsync(seller.McpToken)).Result!.Value
            .GetProperty("tools").EnumerateArray()
            .Where(t => ImportToolNames.Contains(t.GetProperty("name").GetString()))
            .ToList();

        tools.Should().HaveCount(7, "as sete ferramentas de importação do contrato da 008");

        foreach (var tool in tools)
        {
            var name = tool.GetProperty("name").GetString();
            var schema = tool.GetProperty("inputSchema");

            schema.GetProperty("additionalProperties").GetBoolean().Should().BeFalse(name);

            if (schema.TryGetProperty("properties", out var properties))
            {
                properties.EnumerateObject()
                    .Select(p => p.Name)
                    .Intersect(CredentialKeys, StringComparer.OrdinalIgnoreCase)
                    .Should().BeEmpty($"{name} não pode ter campo de credencial (FR-018)");
            }
        }

        foreach (var name in new[] { "iniciar_importacao", "atualizar_base" })
        {
            var description = tools.Single(t => t.GetProperty("name").GetString() == name)
                .GetProperty("description").GetString()!;

            description.Should().Contain("Nunca peça nem repasse a senha", name);
        }
    }

    [Fact]
    public async Task Audit_TracksThePeriodAndModeOfTheConfirmedRun()
    {
        // contracts/mcp-tools.md: a auditoria guarda período e modo (e nunca credencial). A chamada é em
        // simulação, para a trilha ser conferida sem gravar pedido nenhum.
        var seller = await McpSeller.CreateAsync(fixture, "Ana Auditoria MCP", ImportScopes);

        await fixture.SaveCredentialsAsync(seller.User.ApiToken, Login(TestDataHelper.SCENARIO_OK), "v1");

        var started = WriteToolsTests.Structured(await seller.CallAsync(
            "iniciar_importacao",
            new { inicio = SingleDay, fim = SingleDay, simular = true, confirmar = true }));

        var runId = started.GetProperty("registro").GetProperty("importacao_id").GetInt64();

        var entries = await AuditAsync(seller, "iniciar_importacao");

        entries.Should().ContainSingle("prévia não se audita; a confirmação, sim (R16 da 007)");

        var entry = entries.Single();

        entry.GetProperty("outcome").GetString().Should().Be("Recorded");
        entry.GetProperty("arguments").GetProperty("inicio").GetString().Should().Be(SingleDay);
        entry.GetProperty("arguments").GetProperty("fim").GetString().Should().Be(SingleDay);
        entry.GetProperty("arguments").GetProperty("simular").GetBoolean().Should().BeTrue();
        entry.GetProperty("arguments").GetProperty("confirmar").GetBoolean().Should().BeTrue();

        var body = entry.GetRawText();

        body.Should().NotContain(TestDataHelper.TRACEABLE_PASSWORD);
        body.Should().NotContain("s482", "a credencial não é argumento, então não pode estar na trilha");
        body.Should().NotContain("password");

        await fixture.WaitUntilFinishedAsync(runId, seller.User.ApiToken);
    }

    // =====================================================================================
    // acompanhamento (mesmo estado da API)
    // =====================================================================================

    [Fact]
    public async Task Tracking_ReflectsTheApiState()
    {
        var seller = await McpSeller.CreateAsync(fixture, "Ana Acompanha MCP", ImportScopes);

        var current = WriteToolsTests.Structured(await seller.CallAsync("importacao_atual"));
        current.GetProperty("emAndamento").GetBoolean().Should().BeFalse();

        var history = WriteToolsTests.Structured(await seller.CallAsync("listar_importacoes"));
        history.GetProperty("total").GetInt32().Should().Be(0);

        (await seller.CallAsync("obter_importacao", new { importacao_id = long.MaxValue })).IsToolError.Should().BeTrue();
        (await seller.CallAsync("cancelar_importacao", new { importacao_id = long.MaxValue, confirmar = true })).IsToolError.Should().BeTrue();
    }

    // =====================================================================================
    // Helpers
    // =====================================================================================

    private static string Login(string scenario, string? account = null) =>
        TestDataHelper.FakeLogin(scenario, account);

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Total de execuções da vendedora no histórico da API (a prova de que nada foi criado).</summary>
    private static async Task<int> CountRunsAsync(McpSeller seller)
    {
        using var document = await seller.RestGetAsync("/imports/mais-vendas/runs");

        return document.RootElement.GetProperty("totalCount").GetInt32();
    }

    /// <summary>Importação Fake de um dia em gravação, para deixar base a atualizar (FR-011).</summary>
    private async Task ImportOneDayAsync(McpSeller seller)
    {
        var day = DateOnly.Parse(SingleDay, CultureInfo.InvariantCulture);

        var run = await fixture.StartImportAsync(
            seller.User.ApiToken, Login(TestDataHelper.SCENARIO_OK), "v1", day, day);

        (await fixture.WaitUntilFinishedAsync(run.Id, seller.User.ApiToken))
            .Status.Should().Be(ImportRunStatus.Succeeded);
    }

    /// <summary>Trilha de auditoria da ferramenta, lida pelo diagnóstico de homologação (T056 da 007).</summary>
    private async Task<List<JsonElement>> AuditAsync(McpSeller seller, string tool)
    {
        var body = await new Url($"{fixture.McpBaseUrl.TrimEnd('/')}/mcp/diagnostics/audit")
            .SetQueryParam("tool", tool)
            .WithOAuthBearerToken(seller.McpToken)
            .WithTimeout(fixture.TimeoutSeconds)
            .AllowAnyHttpStatus()
            .GetStringAsync();

        return JsonDocument.Parse(body).RootElement.EnumerateArray().ToList();
    }
}
