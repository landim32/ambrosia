using System.Text.Json;
using Ambrosio.ApiTests.Fixtures;
using Ambrosio.ApiTests.Helpers;
using FluentAssertions;
using Flurl;
using Flurl.Http;

namespace Ambrosio.ApiTests.McpServer;

/// <summary>
/// T064 [US5]: importação do +Vendas pelo MCP (R9). A sugestão de período é a mesma da API;
/// <c>iniciar_importacao</c> devolve o link e não cria execução; login e senha são recusados e não
/// chegam à auditoria; o acompanhamento enxerga as execuções da API.
/// </summary>
/// <remarks>
/// Não inicia importação real: o coletor deste ambiente pode ser o Playwright, e a suíte não leva
/// credenciais do +Vendas. O acompanhamento é conferido sobre o histórico existente.
/// </remarks>
[Collection(ApiTestCollection.API_TESTS)]
public class ImportToolsTests(ApiTestFixture fixture)
{
    private const string ImportScopes = "openid email profile imports:read imports:write";

    [Fact]
    public async Task SugestaoPeriodo_ShouldMatchTheApiDefaults()
    {
        var seller = await McpSeller.CreateAsync(fixture, scopes: ImportScopes);

        var tool = WriteToolsTests.Structured(await seller.CallAsync("sugestao_periodo_importacao"));
        using var rest = await seller.RestGetAsync("/imports/mais-vendas/defaults");

        tool.GetProperty("fimSugerido").GetString().Should().Be(rest.RootElement.GetProperty("suggestedEndDate").GetString());
        tool.GetProperty("diasPorBloco").GetInt32().Should().Be(rest.RootElement.GetProperty("blockDays").GetInt32());
        tool.GetProperty("maxDias").GetInt32().Should().Be(rest.RootElement.GetProperty("maxRangeDays").GetInt32());
    }

    [Fact]
    public async Task IniciarImportacao_ReturnsTheLink_AndCreatesNoRun()
    {
        var seller = await McpSeller.CreateAsync(fixture, scopes: ImportScopes);

        var preview = WriteToolsTests.Structured(await seller.CallAsync("iniciar_importacao", new { inicio = "2026-09-26", fim = "2026-10-25" }));
        preview.GetProperty("gravado").GetBoolean().Should().BeFalse();

        var result = WriteToolsTests.Structured(await seller.CallAsync("iniciar_importacao", new { inicio = "2026-09-26", fim = "2026-10-25", confirmar = true }));
        result.GetProperty("registro").GetProperty("link").GetString().Should().EndWith("/importacao?inicio=2026-09-26&fim=2026-10-25");

        using var runs = await seller.RestGetAsync("/imports/mais-vendas/runs");
        runs.RootElement.GetProperty("totalCount").GetInt32().Should().Be(0, "a tool não inicia execução");
    }

    [Theory]
    [InlineData("2026-10-25", "2026-09-26")]
    [InlineData("2020-01-01", "2026-10-25")]
    [InlineData("2099-01-01", "2099-01-02")]
    public async Task IniciarImportacao_WithInvalidPeriod_ReturnsTheValidationMessage(string inicio, string fim)
    {
        var seller = await McpSeller.CreateAsync(fixture, scopes: ImportScopes);

        var result = await seller.CallAsync("iniciar_importacao", new { inicio, fim, confirmar = true });

        result.IsToolError.Should().BeTrue();
        result.Text.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("senha")]
    [InlineData("password")]
    [InlineData("login")]
    public async Task IniciarImportacao_WithCredentials_IsRejectedAndNotAudited(string field)
    {
        var seller = await McpSeller.CreateAsync(fixture, scopes: ImportScopes);
        var args = new Dictionary<string, object>
        {
            ["inicio"] = "2026-09-26",
            ["fim"] = "2026-10-25",
            ["confirmar"] = true,
            [field] = "segredo-que-nao-pode-vazar"
        };

        var result = await seller.CallAsync("iniciar_importacao", args);

        result.IsToolError.Should().BeTrue();
        result.Text.Should().Contain(field);

        var audit = await new Url($"{fixture.McpBaseUrl.TrimEnd('/')}/mcp/diagnostics/audit")
            .SetQueryParam("tool", "iniciar_importacao")
            .WithOAuthBearerToken(seller.McpToken)
            .GetStringAsync();
        audit.Should().NotContain("segredo-que-nao-pode-vazar");
        JsonDocument.Parse(audit).RootElement.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Tracking_ReflectsTheApiState()
    {
        var seller = await McpSeller.CreateAsync(fixture, scopes: ImportScopes);

        var current = WriteToolsTests.Structured(await seller.CallAsync("importacao_atual"));
        current.GetProperty("emAndamento").GetBoolean().Should().BeFalse();

        var history = WriteToolsTests.Structured(await seller.CallAsync("listar_importacoes"));
        history.GetProperty("total").GetInt32().Should().Be(0);

        (await seller.CallAsync("obter_importacao", new { importacao_id = long.MaxValue })).IsToolError.Should().BeTrue();
        (await seller.CallAsync("cancelar_importacao", new { importacao_id = long.MaxValue, confirmar = true })).IsToolError.Should().BeTrue();
    }
}
