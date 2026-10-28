using Ambrosio.ApiTests.Fixtures;

namespace Ambrosio.ApiTests.McpServer;

/// <summary>
/// Conta nova por teste para o fluxo OAuth: cadastrada pela API (<c>POST /auth/register</c>), como a
/// vendedora faria, e usada depois no login do authorization server.
/// </summary>
internal sealed record McpTestUser(long Id, string Email, string Name, string Password)
{
    public static async Task<McpTestUser> CreateAsync(ApiTestFixture fixture, string name = "Vendedora MCP")
    {
        var email = $"mcp+{Guid.NewGuid():N}@exemplo.com";
        var result = await fixture.RegisterAsync(email, name, fixture.TestPassword);

        return new McpTestUser(result.User.Id, email, name, fixture.TestPassword);
    }
}

/// <summary>Claims de um JWT do AS (payload), lidas pelo handler da biblioteca.</summary>
internal static class McpJwt
{
    public static System.Text.Json.JsonDocument Claims(string token) =>
        System.Text.Json.JsonDocument.Parse(
            new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(token).Payload.SerializeToJson());
}
