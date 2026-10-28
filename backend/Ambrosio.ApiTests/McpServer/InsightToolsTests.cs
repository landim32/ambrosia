using System.Text.Json;
using Ambrosio.ApiTests.Fixtures;
using Ambrosio.DTO.Enums;
using FluentAssertions;

namespace Ambrosio.ApiTests.McpServer;

/// <summary>
/// T047 [US2]: consultas prontas — busca por nome e por CPF (sem devolver o CPF), histórico com ticket
/// médio, clientes inativos com filtros, resumo do período (PA/VA conferidos à mão) e devoluções e
/// cancelamentos por tipo.
/// </summary>
[Collection(ApiTestCollection.API_TESTS)]
public class InsightToolsTests(ApiTestFixture fixture)
{
    [Fact]
    public async Task BuscarCliente_ByNameAndByCpf_ShouldFindWithoutReturningTheCpf()
    {
        var seller = await McpSeller.CreateAsync(fixture);
        var (customerId, cpf) = await seller.CreateCustomerAsync("Helena Busca");

        var byName = Structured(await seller.CallAsync("buscar_cliente", new { nome = "helena" }));
        byName.GetProperty("itens").EnumerateArray().Select(c => c.GetProperty("id").GetInt64()).Should().Contain(customerId);

        var byCpfResult = await seller.CallAsync("buscar_cliente", new { cpf });
        var byCpf = Structured(byCpfResult);
        byCpf.GetProperty("total").GetInt32().Should().Be(1);
        byCpf.GetProperty("itens")[0].GetProperty("id").GetInt64().Should().Be(customerId);
        byCpfResult.RawBody.Should().NotContain(new string(cpf.Where(char.IsDigit).ToArray()));

        var none = await seller.CallAsync("buscar_cliente", new { });
        none.IsToolError.Should().BeTrue("é preciso informar nome ou CPF");
    }

    [Fact]
    public async Task HistoricoCliente_ShouldReturnOrdersWithItems_AverageTicketAndLastPurchase()
    {
        var seller = await McpSeller.CreateAsync(fixture);
        var (customerId, _) = await seller.CreateCustomerAsync("Iris Histórico");
        await seller.CreateOrderAsync(customerId, new DateOnly(2026, 2, 1), OrderKind.Sale, 300m, 2);
        await seller.CreateOrderAsync(customerId, new DateOnly(2026, 3, 1), OrderKind.Sale, 500m, 1);

        var data = Structured(await seller.CallAsync("historico_cliente", new { cliente_id = customerId }));

        data.GetProperty("ticketMedio").GetDecimal().Should().Be(400m);
        data.GetProperty("resumo").GetProperty("ultimaCompra").GetString().Should().Be("2026-03-01");
        var orders = data.GetProperty("pedidos").EnumerateArray().ToList();
        orders.Should().HaveCount(2);
        orders[0].GetProperty("data").GetString().Should().Be("2026-03-01", "mais recentes primeiro");
        orders[1].GetProperty("itens").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task ClientesInativos_ShouldApplyDaysMinPurchasesAndMinTotal()
    {
        var seller = await McpSeller.CreateAsync(fixture);
        var longAgo = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-120));
        var recent = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-5));

        var (loyal, _) = await seller.CreateCustomerAsync("Joana Fiel");
        await seller.CreateOrderAsync(loyal, longAgo, OrderKind.Sale, 3000m);
        await seller.CreateOrderAsync(loyal, longAgo, OrderKind.Sale, 3000m);

        var (occasional, _) = await seller.CreateCustomerAsync("Karla Ocasional");
        await seller.CreateOrderAsync(occasional, longAgo, OrderKind.Sale, 1000m);

        var (active, _) = await seller.CreateCustomerAsync("Lia Ativa");
        await seller.CreateOrderAsync(active, recent, OrderKind.Sale, 9000m);

        await seller.CreateCustomerAsync("Marta Sem Compra");

        Ids(await seller.CallAsync("clientes_inativos", new { dias = 90 })).Should().BeEquivalentTo([loyal, occasional]);
        Ids(await seller.CallAsync("clientes_inativos", new { dias = 90, min_compras = 2 })).Should().BeEquivalentTo([loyal]);
        Ids(await seller.CallAsync("clientes_inativos", new { dias = 90, min_total = 5000 })).Should().BeEquivalentTo([loyal]);

        var invalid = await seller.CallAsync("clientes_inativos", new { dias = 0 });
        invalid.IsToolError.Should().BeTrue();
    }

    [Fact]
    public async Task ResumoPeriodo_ShouldComputeNetTotalSalesPiecesPaAndVa()
    {
        var seller = await McpSeller.CreateAsync(fixture);
        var (customerId, _) = await seller.CreateCustomerAsync();
        await seller.CreateOrderAsync(customerId, new DateOnly(2025, 1, 10), OrderKind.Sale, 300m, 2);
        await seller.CreateOrderAsync(customerId, new DateOnly(2025, 1, 12), OrderKind.Sale, 200m, 1);
        await seller.CreateOrderAsync(customerId, new DateOnly(2025, 1, 15), OrderKind.Return, -100m, 1);
        await seller.CreateOrderAsync(customerId, new DateOnly(2025, 2, 10), OrderKind.Sale, 999m, 1);

        var data = Structured(await seller.CallAsync("resumo_periodo", new { inicio = "2025-01-01", fim = "2025-01-31" }));

        // Líquido = 300 + 200 - 100; atendimentos = 2 vendas; peças = 3 itens das vendas.
        data.GetProperty("totalLiquido").GetDecimal().Should().Be(400m);
        data.GetProperty("atendimentos").GetInt32().Should().Be(2);
        data.GetProperty("pecas").GetInt32().Should().Be(3);
        data.GetProperty("pa").GetDecimal().Should().Be(1.5m);
        data.GetProperty("va").GetDecimal().Should().Be(200m);
    }

    [Fact]
    public async Task ResumoPeriodo_WithoutDates_ShouldUseTheCurrentQuotaMonth()
    {
        var seller = await McpSeller.CreateAsync(fixture);

        var data = Structured(await seller.CallAsync("resumo_periodo"));

        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-3));
        var closing = today.Day > 25 ? today.AddMonths(1) : today;
        var expectedTo = new DateOnly(closing.Year, closing.Month, 25);

        data.GetProperty("fim").GetString().Should().Be(expectedTo.ToString("yyyy-MM-dd"));
        data.GetProperty("inicio").GetString().Should().Be(expectedTo.AddMonths(-1).AddDays(1).ToString("yyyy-MM-dd"));
        data.GetProperty("atendimentos").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task DevolucoesCancelamentos_ShouldCountAndSumByKind()
    {
        var seller = await McpSeller.CreateAsync(fixture);
        var (customerId, _) = await seller.CreateCustomerAsync();
        await seller.CreateOrderAsync(customerId, new DateOnly(2025, 4, 3), OrderKind.Sale, 800m);
        await seller.CreateOrderAsync(customerId, new DateOnly(2025, 4, 5), OrderKind.Return, -120m);
        await seller.CreateOrderAsync(customerId, new DateOnly(2025, 4, 6), OrderKind.Return, -80m);
        await seller.CreateOrderAsync(customerId, new DateOnly(2025, 4, 7), OrderKind.Cancellation, -50m);

        var data = Structured(await seller.CallAsync("devolucoes_cancelamentos", new { inicio = "2025-04-01", fim = "2025-04-30" }));

        data.GetProperty("devolucoes").GetProperty("quantidade").GetInt32().Should().Be(2);
        data.GetProperty("devolucoes").GetProperty("total").GetDecimal().Should().Be(-200m);
        data.GetProperty("cancelamentos").GetProperty("quantidade").GetInt32().Should().Be(1);
        data.GetProperty("cancelamentos").GetProperty("total").GetDecimal().Should().Be(-50m);
    }

    private static List<long> Ids(Helpers.McpCallResult result) =>
        Structured(result).GetProperty("itens").EnumerateArray().Select(c => c.GetProperty("id").GetInt64()).ToList();

    private static JsonElement Structured(Helpers.McpCallResult result)
    {
        result.StatusCode.Should().Be(200, result.RawBody);
        result.IsToolError.Should().BeFalse(result.Text);

        return result.StructuredContent!.Value;
    }
}
