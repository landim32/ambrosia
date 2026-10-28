using System.Text.Json;
using Ambrosio.ApiTests.Fixtures;
using Ambrosio.ApiTests.Helpers;
using Ambrosio.DTO.Enums;
using FluentAssertions;

namespace Ambrosio.ApiTests.McpServer;

/// <summary>
/// T055 [US3]: tools de escrita com prévia e confirmação (R11). Sem <c>confirmar</c> nada muda e vem a
/// prévia; com <c>confirmar: true</c> grava e a API mostra o resultado; os erros de regra trazem a
/// mesma mensagem da API.
/// </summary>
[Collection(ApiTestCollection.API_TESTS)]
public class WriteToolsTests(ApiTestFixture fixture)
{
    public const string WriteScopes = "openid email profile customers:read customers:write orders:read orders:write";

    private static readonly DateOnly OrderDay = new(2026, 3, 10);

    [Fact]
    public async Task CadastrarCliente_PreviewDoesNothing_ConfirmCreates()
    {
        var seller = await McpSeller.CreateAsync(fixture, scopes: WriteScopes);
        var cpf = TestDataHelper.GenerateValidCpf();

        var preview = await seller.CallAsync("cadastrar_cliente", new { nome = "Nina Prévia", cpf });
        AssertPreview(preview);
        (await fixture.CountCustomersAsync(seller.User.ApiToken)).Should().Be(0);
        preview.RawBody.Should().NotContain(new string(cpf.Where(char.IsDigit).ToArray()));

        var created = Structured(await seller.CallAsync("cadastrar_cliente", new { nome = "Nina Prévia", cpf, confirmar = true }));
        created.GetProperty("gravado").GetBoolean().Should().BeTrue();
        var id = created.GetProperty("registro").GetProperty("id").GetInt64();

        using var rest = await seller.RestGetAsync($"/customers/{id}");
        rest.RootElement.GetProperty("name").GetString().Should().Be("Nina Prévia");
    }

    [Theory]
    [InlineData("123.456.789-00")]
    [InlineData("duplicado")]
    public async Task CadastrarCliente_InvalidOrDuplicateCpf_ReturnsTheApiMessage(string cpfCase)
    {
        var seller = await McpSeller.CreateAsync(fixture, scopes: WriteScopes);
        var cpf = cpfCase;
        if (cpfCase == "duplicado")
        {
            (_, cpf) = await seller.CreateCustomerAsync("Original");
        }

        var api = await fixture.PostAsync("/customers", seller.User.ApiToken, new { name = "Outra", cpf });
        var apiMessage = ApiMessage(api.Body);

        foreach (var confirmar in new[] { false, true })
        {
            var result = await seller.CallAsync("cadastrar_cliente", new { nome = "Outra", cpf, confirmar });
            result.IsToolError.Should().BeTrue();
            result.Text.Should().Contain(apiMessage);
        }
    }

    [Fact]
    public async Task AtualizarCliente_PreviewShowsBeforeAndAfter_ConfirmRenames()
    {
        var seller = await McpSeller.CreateAsync(fixture, scopes: WriteScopes);
        var (id, _) = await seller.CreateCustomerAsync("Olga Antes");

        var preview = Structured(await seller.CallAsync("atualizar_cliente", new { cliente_id = id, nome = "Olga Depois" }));
        preview.GetProperty("previa").GetProperty("antes").GetProperty("nome").GetString().Should().Be("Olga Antes");
        preview.GetProperty("previa").GetProperty("depois").GetProperty("nome").GetString().Should().Be("Olga Depois");
        using (var unchanged = await seller.RestGetAsync($"/customers/{id}"))
        {
            unchanged.RootElement.GetProperty("name").GetString().Should().Be("Olga Antes");
        }

        Structured(await seller.CallAsync("atualizar_cliente", new { cliente_id = id, nome = "Olga Depois", confirmar = true }));
        using var renamed = await seller.RestGetAsync($"/customers/{id}");
        renamed.RootElement.GetProperty("name").GetString().Should().Be("Olga Depois");
    }

    [Fact]
    public async Task CadastrarPedido_PreviewDoesNothing_ConfirmCreates()
    {
        var seller = await McpSeller.CreateAsync(fixture, scopes: WriteScopes);
        var (customerId, _) = await seller.CreateCustomerAsync("Paula Pedido");
        var args = OrderArgs(customerId, "venda", 250m);

        AssertPreview(await seller.CallAsync("cadastrar_pedido", args));
        (await fixture.CountOrdersAsync(seller.User.ApiToken)).Should().Be(0);

        var created = Structured(await seller.CallAsync("cadastrar_pedido", OrderArgs(customerId, "venda", 250m, confirmar: true)));
        var id = created.GetProperty("registro").GetProperty("id").GetInt64();

        using var rest = await seller.RestGetAsync($"/orders/{id}");
        rest.RootElement.GetProperty("total").GetDecimal().Should().Be(250m);
        rest.RootElement.GetProperty("items").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task CadastrarPedido_WrongTotalSign_ReturnsTheApiMessage()
    {
        var seller = await McpSeller.CreateAsync(fixture, scopes: WriteScopes);
        var (customerId, _) = await seller.CreateCustomerAsync();

        var api = await fixture.PostAsync("/orders", seller.User.ApiToken, new
        {
            customerId,
            orderNumber = TestDataHelper.NewOrderNumber(),
            orderDate = OrderDay,
            kind = "return",
            total = 100m,
            items = new[] { new { productName = "Peça", price = 100m } }
        });

        var result = await seller.CallAsync("cadastrar_pedido", OrderArgs(customerId, "devolucao", 100m));

        result.IsToolError.Should().BeTrue();
        result.Text.Should().Contain(ApiMessage(api.Body));
    }

    [Fact]
    public async Task AtualizarPedido_PreviewShowsBeforeAndAfter_ConfirmUpdates()
    {
        var seller = await McpSeller.CreateAsync(fixture, scopes: WriteScopes);
        var (customerId, _) = await seller.CreateCustomerAsync();
        var orderId = await seller.CreateOrderAsync(customerId, OrderDay, OrderKind.Sale, 300m);

        var preview = Structured(await seller.CallAsync("atualizar_pedido", new { pedido_id = orderId, total = 350m }));
        preview.GetProperty("previa").GetProperty("antes").GetProperty("total").GetDecimal().Should().Be(300m);
        preview.GetProperty("previa").GetProperty("depois").GetProperty("total").GetDecimal().Should().Be(350m);

        Structured(await seller.CallAsync("atualizar_pedido", new { pedido_id = orderId, total = 350m, status = "Entregue", confirmar = true }));
        using var rest = await seller.RestGetAsync($"/orders/{orderId}");
        rest.RootElement.GetProperty("total").GetDecimal().Should().Be(350m);
        rest.RootElement.GetProperty("status").GetString().Should().Be("Entregue");
    }

    [Fact]
    public async Task WriteTools_OnTheOtherSellersRecords_AreNotFound()
    {
        var ana = await McpSeller.CreateAsync(fixture, "Ana Escrita", WriteScopes);
        var bia = await McpSeller.CreateAsync(fixture, "Bia Escrita", WriteScopes);
        var (anaCustomer, _) = await ana.CreateCustomerAsync("Cliente da Ana");
        var anaOrder = await ana.CreateOrderAsync(anaCustomer, OrderDay, OrderKind.Sale, 300m);

        foreach (var confirmar in new[] { false, true })
        {
            (await bia.CallAsync("atualizar_cliente", new { cliente_id = anaCustomer, nome = "Invasão", confirmar })).IsToolError.Should().BeTrue();
            (await bia.CallAsync("atualizar_pedido", new { pedido_id = anaOrder, total = 1m, confirmar })).IsToolError.Should().BeTrue();
            (await bia.CallAsync("cadastrar_pedido", OrderArgs(anaCustomer, "venda", 10m, confirmar))).IsToolError.Should().BeTrue();
        }

        using var rest = await ana.RestGetAsync($"/customers/{anaCustomer}");
        rest.RootElement.GetProperty("name").GetString().Should().Be("Cliente da Ana");
    }

    private static object OrderArgs(long customerId, string tipo, decimal total, bool confirmar = false) => new
    {
        cliente_id = customerId,
        numero = TestDataHelper.NewOrderNumber(),
        data = OrderDay.ToString("yyyy-MM-dd"),
        tipo,
        total,
        canal = "Linx",
        venda_original_em = tipo == "venda" ? null : OrderDay.ToString("yyyy-MM-dd"),
        itens = new[]
        {
            new { produto = "Calça", referencia = "REF1", tamanho = "38", preco = total / 2 },
            new { produto = "Blusa", referencia = "REF2", tamanho = "P", preco = total / 2 }
        },
        confirmar
    };

    private static void AssertPreview(McpCallResult result)
    {
        var data = Structured(result);
        data.GetProperty("gravado").GetBoolean().Should().BeFalse();
        result.Text.Should().Contain("Nada foi gravado");
    }

    private static string ApiMessage(string body)
    {
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;

        if (root.TryGetProperty("erros", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
        {
            return errors[0].GetProperty("mensagem").GetString()!;
        }

        return root.GetProperty("mensagem").GetString()!;
    }

    internal static JsonElement Structured(McpCallResult result)
    {
        result.StatusCode.Should().Be(200, result.RawBody);
        result.IsToolError.Should().BeFalse(result.Text);

        return result.StructuredContent!.Value;
    }
}
