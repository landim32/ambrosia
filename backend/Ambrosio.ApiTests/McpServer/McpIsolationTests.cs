using Ambrosio.ApiTests.Fixtures;
using Ambrosio.DTO.Enums;
using FluentAssertions;

namespace Ambrosio.ApiTests.McpServer;

/// <summary>
/// T048 [US2]: isolamento entre vendedoras no MCP (constituição §VI). Nenhuma tool de leitura devolve
/// dado da outra, e o id de um recurso da outra responde igual a um id inexistente.
/// </summary>
[Collection(ApiTestCollection.API_TESTS)]
public class McpIsolationTests(ApiTestFixture fixture)
{
    [Fact]
    public async Task ReadTools_ShouldNeverReturnTheOtherSellersData()
    {
        var ana = await McpSeller.CreateAsync(fixture, "Ana Isolamento");
        var bia = await McpSeller.CreateAsync(fixture, "Bia Isolamento");

        var (anaCustomer, anaCpf) = await ana.CreateCustomerAsync("Cliente Secreta da Ana");
        var anaOrder = await ana.CreateOrderAsync(anaCustomer, new DateOnly(2025, 5, 5), OrderKind.Sale, 700m, 2);

        var calls = new (string Tool, object Args)[]
        {
            ("listar_clientes", new { tamanho = 100 }),
            ("listar_pedidos", new { tamanho = 100 }),
            ("buscar_cliente", new { nome = "Secreta" }),
            ("buscar_cliente", new { cpf = anaCpf }),
            ("clientes_inativos", new { dias = 1 }),
            ("resumo_periodo", new { inicio = "2025-05-01", fim = "2025-05-31" })
        };

        foreach (var (tool, args) in calls)
        {
            var result = await bia.CallAsync(tool, args);

            result.IsToolError.Should().BeFalse($"{tool}: {result.Text}");
            result.RawBody.Should().NotContain("Cliente Secreta da Ana", tool);
            result.RawBody.Should().NotContain($"\"id\":{anaOrder},", tool);
        }

        var summary = (await bia.CallAsync("resumo_periodo", new { inicio = "2025-05-01", fim = "2025-05-31" })).StructuredContent!.Value;
        summary.GetProperty("atendimentos").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task OtherSellersIds_ShouldLookExactlyLikeMissingIds()
    {
        var ana = await McpSeller.CreateAsync(fixture, "Ana Isolamento");
        var bia = await McpSeller.CreateAsync(fixture, "Bia Isolamento");

        var (anaCustomer, _) = await ana.CreateCustomerAsync();
        var anaOrder = await ana.CreateOrderAsync(anaCustomer, new DateOnly(2025, 5, 5), OrderKind.Sale, 700m);

        var cases = new (string Tool, object Foreign, object Missing)[]
        {
            ("obter_cliente", new { cliente_id = anaCustomer }, new { cliente_id = long.MaxValue }),
            ("historico_cliente", new { cliente_id = anaCustomer }, new { cliente_id = long.MaxValue }),
            ("obter_pedido", new { pedido_id = anaOrder }, new { pedido_id = long.MaxValue }),
            ("listar_itens_pedido", new { pedido_id = anaOrder }, new { pedido_id = long.MaxValue })
        };

        foreach (var (tool, foreign, missing) in cases)
        {
            var fromOther = await bia.CallAsync(tool, foreign);
            var notFound = await bia.CallAsync(tool, missing);

            fromOther.IsToolError.Should().BeTrue(tool);
            notFound.IsToolError.Should().BeTrue(tool);
            fromOther.Text.Should().Be(notFound.Text, $"{tool}: recurso de outra vendedora = inexistente");
        }
    }
}
