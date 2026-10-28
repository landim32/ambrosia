using System.Text.Json;
using Ambrosio.Domain.Interfaces;

namespace Ambrosio.McpServer.Mcp.Diagnostics;

/// <summary>
/// Leitura da trilha de auditoria do próprio usuário (feature 007, T059), para o teste de API conferir
/// o que as tools de escrita gravaram. Mapeado só em Development e Homolog: em produção a rota não
/// existe.
/// </summary>
/// <remarks>
/// Fica sob <c>/mcp</c> (e não em <c>/mcp-diagnostics</c>, como a tarefa previa) porque é o prefixo
/// que o gateway já encaminha a este host. Autenticado pelo token do AS (o mesmo JwtBearer do MCP) e
/// filtrado pelo <c>sub</c>: ninguém lê a trilha de outra vendedora.
/// </remarks>
public static class AuditDiagnosticsEndpoint
{
    public const string Path = "/mcp/diagnostics/audit";

    public static IEndpointRouteBuilder MapAmbrosioAuditDiagnostics(this IEndpointRouteBuilder endpoints, IHostEnvironment environment)
    {
        if (!environment.IsDevelopment() && !environment.IsEnvironment("Homolog"))
        {
            return endpoints;
        }

        endpoints.MapGet(Path, async (HttpContext context, IMcpAuditService audit, string? tool) =>
        {
            if (!long.TryParse(context.User.FindFirst("sub")?.Value, out var userId))
            {
                return Microsoft.AspNetCore.Http.Results.Unauthorized();
            }

            var entries = await audit.ListRecentAsync(userId, tool, 50);

            return Microsoft.AspNetCore.Http.Results.Ok(entries.Select(e => new
            {
                tool = e.ToolName,
                outcome = e.Outcome.ToString(),
                clientId = e.ClientId,
                arguments = JsonDocument.Parse(e.Arguments).RootElement,
                resultSummary = e.ResultSummary,
                createdAt = e.CreatedAt
            }));
        });

        return endpoints;
    }
}
