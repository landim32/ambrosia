using System.Text.Json;
using Ambrosio.ApiTests.Fixtures;
using Ambrosio.ApiTests.Helpers;
using Ambrosio.DTO.Customers;
using Ambrosio.DTO.Enums;
using Ambrosio.DTO.Orders;

namespace Ambrosio.ApiTests.McpServer;

/// <summary>
/// Conta nova por teste para o fluxo OAuth: cadastrada pela API (<c>POST /auth/register</c>), como a
/// vendedora faria, e usada depois no login do authorization server.
/// </summary>
internal sealed record McpTestUser(long Id, string Email, string Name, string Password, string ApiToken)
{
    public static async Task<McpTestUser> CreateAsync(ApiTestFixture fixture, string name = "Vendedora MCP")
    {
        var email = $"mcp+{Guid.NewGuid():N}@exemplo.com";
        var result = await fixture.RegisterAsync(email, name, fixture.TestPassword);

        return new McpTestUser(result.User.Id, email, name, fixture.TestPassword, result.Token);
    }
}

/// <summary>
/// Vendedora com dados criados pela API REST e um token do AS para o MCP: compara-se a tool com a
/// resposta REST do mesmo usuário (T046–T048).
/// </summary>
internal sealed class McpSeller
{
    public const string ReadScopes = "openid email profile customers:read orders:read";

    private readonly ApiTestFixture _fixture;

    private McpSeller(ApiTestFixture fixture, McpTestUser user, string mcpToken)
    {
        _fixture = fixture;
        User = user;
        McpToken = mcpToken;
        Mcp = new McpTestClient(fixture);
    }

    public McpTestUser User { get; }

    public string McpToken { get; }

    public McpTestClient Mcp { get; }

    public static async Task<McpSeller> CreateAsync(ApiTestFixture fixture, string name = "Vendedora MCP", string scopes = ReadScopes)
    {
        var user = await McpTestUser.CreateAsync(fixture, name);
        var (_, tokens) = await new OAuthFlowHelper(fixture).GetTokensAsync(user.Email, user.Password, scopes);

        return new McpSeller(fixture, user, tokens.AccessToken);
    }

    public Task<McpCallResult> CallAsync(string tool, object? arguments = null) =>
        Mcp.CallToolAsync(tool, arguments, McpToken);

    public async Task<JsonDocument> RestGetAsync(string path)
    {
        var result = await _fixture.GetAsync(path, User.ApiToken);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"GET {path} respondeu {result.StatusCode}: {result.Body}");
        }

        return JsonDocument.Parse(result.Body);
    }

    public async Task<(long Id, string Cpf)> CreateCustomerAsync(string? name = null)
    {
        var customer = TestDataHelper.NewCustomer(name);
        var result = await _fixture.PostAsync("/customers", User.ApiToken, customer);
        if (result.StatusCode != 201)
        {
            throw new InvalidOperationException($"POST /customers respondeu {result.StatusCode}: {result.Body}");
        }

        using var json = JsonDocument.Parse(result.Body);

        return (json.RootElement.GetProperty("id").GetInt64(), customer.Cpf);
    }

    /// <summary>Pedido com <paramref name="itemCount"/> itens; preço dos itens com o mesmo sinal do total.</summary>
    public async Task<long> CreateOrderAsync(long customerId, DateOnly date, OrderKind kind, decimal total, int itemCount = 1)
    {
        var sign = total < 0 ? -1 : 1;
        var order = new OrderInsertInfo
        {
            CustomerId = customerId,
            OrderNumber = TestDataHelper.NewOrderNumber(),
            OrderDate = date,
            Kind = kind,
            Channel = "Linx",
            Status = kind == OrderKind.Sale ? "Faturado" : "Devolvido",
            Total = total,
            OriginalSaleOn = kind == OrderKind.Sale ? null : date,
            CancelledOn = kind == OrderKind.Cancellation ? date : null,
            Items = Enumerable.Range(1, itemCount).Select(i => new OrderItemInsertInfo
            {
                ProductName = $"Peça {i}",
                SkuRef = $"REF{i:000}",
                Size = "M",
                Price = sign * Math.Abs(total) / itemCount
            }).ToList()
        };

        var result = await _fixture.PostAsync("/orders", User.ApiToken, order);
        if (result.StatusCode != 201)
        {
            throw new InvalidOperationException($"POST /orders respondeu {result.StatusCode}: {result.Body}");
        }

        using var json = JsonDocument.Parse(result.Body);

        return json.RootElement.GetProperty("id").GetInt64();
    }
}

/// <summary>Claims de um JWT do AS (payload), lidas pelo handler da biblioteca.</summary>
internal static class McpJwt
{
    public static JsonDocument Claims(string token) =>
        JsonDocument.Parse(
            new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(token).Payload.SerializeToJson());
}
