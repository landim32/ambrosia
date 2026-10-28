using System.Text.Json;
using Ambrosio.ApiTests.Fixtures;
using Ambrosio.DTO.Enums;
using FluentAssertions;

namespace Ambrosio.ApiTests.McpServer;

/// <summary>
/// T046 [US2]: as tools de leitura devolvem os mesmos dados das rotas REST equivalentes, com CPF só
/// mascarado e <c>consultado_em</c> em toda resposta.
/// </summary>
[Collection(ApiTestCollection.API_TESTS)]
public class ReadToolsTests(ApiTestFixture fixture)
{
    private static readonly DateOnly OrderDay = new(2026, 3, 10);

    [Fact]
    public async Task ObterUsuarioAtual_ShouldMatchUsersMe()
    {
        var seller = await McpSeller.CreateAsync(fixture, "Vendedora Leitura");

        var tool = await seller.CallAsync("obter_usuario_atual");
        using var rest = await seller.RestGetAsync("/users/me");

        var data = Structured(tool);
        data.GetProperty("id").GetInt64().Should().Be(rest.RootElement.GetProperty("id").GetInt64());
        data.GetProperty("nome").GetString().Should().Be(rest.RootElement.GetProperty("name").GetString());
        data.GetProperty("email").GetString().Should().Be(rest.RootElement.GetProperty("email").GetString());
        data.TryGetProperty("consultado_em", out _).Should().BeTrue();
    }

    [Fact]
    public async Task ListarClientes_ShouldMatchGetCustomers_WithMaskedCpfOnly()
    {
        var seller = await McpSeller.CreateAsync(fixture);
        var (_, cpfAna) = await seller.CreateCustomerAsync("Ana Leitura");
        await seller.CreateCustomerAsync("Bia Leitura");

        var tool = await seller.CallAsync("listar_clientes", new { tamanho = 100 });
        using var rest = await seller.RestGetAsync("/customers?pageSize=100");

        var data = Structured(tool);
        var restItems = rest.RootElement.GetProperty("items").EnumerateArray().ToList();

        data.GetProperty("total").GetInt32().Should().Be(rest.RootElement.GetProperty("totalCount").GetInt32());
        data.GetProperty("itens").EnumerateArray().Select(c => (c.GetProperty("id").GetInt64(), c.GetProperty("nome").GetString(), c.GetProperty("cpfMasked").GetString()))
            .Should().Equal(restItems.Select(c => (c.GetProperty("id").GetInt64(), c.GetProperty("name").GetString(), c.GetProperty("cpfMasked").GetString())));
        data.GetProperty("tem_mais").GetBoolean().Should().BeFalse();

        AssertNoFullCpf(tool, cpfAna);
    }

    [Fact]
    public async Task ObterCliente_ShouldMatchGetCustomerById_WithoutTheFullCpf()
    {
        var seller = await McpSeller.CreateAsync(fixture);
        var (customerId, cpf) = await seller.CreateCustomerAsync("Carla Leitura");
        await seller.CreateOrderAsync(customerId, OrderDay, OrderKind.Sale, 300m, 2);

        var tool = await seller.CallAsync("obter_cliente", new { cliente_id = customerId });
        using var rest = await seller.RestGetAsync($"/customers/{customerId}");

        var data = Structured(tool);
        var restSummary = rest.RootElement.GetProperty("summary");
        data.GetProperty("nome").GetString().Should().Be(rest.RootElement.GetProperty("name").GetString());
        data.GetProperty("cpfMasked").GetString().Should().Be(rest.RootElement.GetProperty("cpfMasked").GetString());
        data.GetProperty("resumo").GetProperty("compras").GetInt32().Should().Be(restSummary.GetProperty("purchases").GetInt32());
        data.GetProperty("resumo").GetProperty("totalLiquido").GetDecimal().Should().Be(restSummary.GetProperty("netTotal").GetDecimal());
        data.GetProperty("resumo").GetProperty("ultimaCompra").GetString().Should().Be(restSummary.GetProperty("lastPurchase").GetString());
        data.TryGetProperty("cpf", out _).Should().BeFalse("o detalhe REST traz o CPF completo; a tool não");

        AssertNoFullCpf(tool, cpf);
    }

    [Fact]
    public async Task ListarPedidos_ShouldMatchGetOrders_IncludingKindFilter()
    {
        var seller = await McpSeller.CreateAsync(fixture);
        var (customerId, _) = await seller.CreateCustomerAsync();
        await seller.CreateOrderAsync(customerId, OrderDay, OrderKind.Sale, 500m, 2);
        await seller.CreateOrderAsync(customerId, OrderDay, OrderKind.Return, -100m);

        var all = Structured(await seller.CallAsync("listar_pedidos", new { tamanho = 100 }));
        using var restAll = await seller.RestGetAsync("/orders?pageSize=100");

        all.GetProperty("itens").EnumerateArray().Select(o => (o.GetProperty("id").GetInt64(), o.GetProperty("total").GetDecimal()))
            .Should().Equal(restAll.RootElement.GetProperty("items").EnumerateArray()
                .Select(o => (o.GetProperty("id").GetInt64(), o.GetProperty("total").GetDecimal())));

        var returns = Structured(await seller.CallAsync("listar_pedidos", new { tipo = "devolucao" }));
        returns.GetProperty("total").GetInt32().Should().Be(1);
        returns.GetProperty("itens")[0].GetProperty("tipo").GetString().Should().Be("devolucao");
    }

    [Fact]
    public async Task ObterPedido_AndListarItens_ShouldMatchGetOrderById()
    {
        var seller = await McpSeller.CreateAsync(fixture);
        var (customerId, cpf) = await seller.CreateCustomerAsync();
        var orderId = await seller.CreateOrderAsync(customerId, OrderDay, OrderKind.Sale, 600m, 3);

        var order = await seller.CallAsync("obter_pedido", new { pedido_id = orderId });
        var items = await seller.CallAsync("listar_itens_pedido", new { pedido_id = orderId });
        using var rest = await seller.RestGetAsync($"/orders/{orderId}");

        var data = Structured(order);
        data.GetProperty("numero").GetString().Should().Be(rest.RootElement.GetProperty("orderNumber").GetString());
        data.GetProperty("total").GetDecimal().Should().Be(600m);
        data.GetProperty("tipo").GetString().Should().Be("venda");
        data.GetProperty("cliente").GetProperty("cpfMasked").GetString().Should().NotBeNullOrEmpty();
        data.TryGetProperty("capturado_em", out _).Should().BeFalse("pedido manual não tem leitura da origem");

        Structured(items).GetProperty("itens").EnumerateArray().Select(i => i.GetProperty("linha").GetInt32())
            .Should().Equal(rest.RootElement.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("lineNo").GetInt32()));

        AssertNoFullCpf(order, cpf);
    }

    [Fact]
    public async Task ListOver200Rows_ShouldReturn200AndTemMais()
    {
        var seller = await McpSeller.CreateAsync(fixture, "Vendedora Volume");
        var lastSale = new DateOnly(2025, 1, 15);

        for (var batch = 0; batch < 201; batch += 20)
        {
            await Task.WhenAll(Enumerable.Range(batch, Math.Min(20, 201 - batch)).Select(async i =>
            {
                var (customerId, _) = await seller.CreateCustomerAsync($"Cliente Volume {i:000}");
                await seller.CreateOrderAsync(customerId, lastSale, OrderKind.Sale, 100m);
            }));
        }

        var result = Structured(await seller.CallAsync("clientes_inativos", new { dias = 30 }));

        result.GetProperty("itens").GetArrayLength().Should().Be(200);
        result.GetProperty("total").GetInt32().Should().Be(201);
        result.GetProperty("tem_mais").GetBoolean().Should().BeTrue();
    }

    private static JsonElement Structured(Helpers.McpCallResult result)
    {
        result.StatusCode.Should().Be(200, result.RawBody);
        result.IsToolError.Should().BeFalse(result.Text);

        return result.StructuredContent!.Value;
    }

    private static void AssertNoFullCpf(Helpers.McpCallResult result, string cpf)
    {
        var digits = new string(cpf.Where(char.IsDigit).ToArray());
        result.RawBody.Should().NotContain(digits).And.NotContain(cpf);
    }
}
