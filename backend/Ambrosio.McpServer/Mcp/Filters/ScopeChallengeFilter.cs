using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ambrosio.DTO.Settings;
using Ambrosio.McpServer.Auth;
using Ambrosio.McpServer.Mcp.Attributes;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Ambrosio.McpServer.Mcp.Filters;

/// <summary>
/// Ponto único de checagem de conta e escopo das tools (feature 007, T022, R6/R7).
/// </summary>
/// <remarks>
/// O <c>/mcp</c> é anônimo para <c>tools/list</c> e para a tool <c>noauth</c>; quem barra tool
/// protegida é este filtro, devolvendo o resultado de desafio que o ChatGPT transforma na tela de
/// vincular conta (sem token) ou de ampliar a autorização (sem escopo). Token presente e inválido
/// nem chega aqui: a esteira HTTP já respondeu 401 (<c>McpTokenGuard</c>).
/// </remarks>
public static class ScopeChallengeFilter
{
    public static McpRequestFilter<CallToolRequestParams, CallToolResult> Create() => next => async (context, cancellationToken) =>
    {
        if (context.MatchedPrimitive is not McpServerTool tool)
        {
            return await next(context, cancellationToken);
        }

        var services = context.Services
            ?? throw new InvalidOperationException("O MCP precisa de escopo de serviços por requisição.");
        var userContext = services.GetRequiredService<McpUserContext>();

        userContext.ToolName = tool.ProtocolTool.Name;
        userContext.Arguments = context.Params?.Arguments is { } args
            ? new Dictionary<string, JsonElement>(args)
            : new Dictionary<string, JsonElement>();

        // Schema fechado (additionalProperties: false no descritor): argumento desconhecido é recusado
        // antes de qualquer coisa — sem chegar à tool nem à auditoria. É o que impede, por exemplo,
        // `senha` de entrar em `iniciar_importacao`.
        var accepted = tool.ProtocolTool.InputSchema.TryGetProperty("properties", out var properties)
            ? properties.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal)
            : [];
        var unknown = userContext.Arguments.Keys.Where(k => !accepted.Contains(k)).ToList();
        if (unknown.Count > 0)
        {
            return ErrorResult($"Argumento não aceito por {tool.ProtocolTool.Name}: {string.Join(", ", unknown)}.");
        }

        var noAuth = tool.Metadata.OfType<AmbrosioNoAuthAttribute>().Any();
        var required = tool.Metadata.OfType<AmbrosioScopesAttribute>().FirstOrDefault()?.Scopes;

        if (noAuth)
        {
            return await next(context, cancellationToken);
        }

        // Falha fechada: uma tool nova sem declaração de escopo não pode virar pública por esquecimento.
        if (required is null || required.Count == 0)
        {
            return ErrorResult($"A tool '{tool.ProtocolTool.Name}' está sem escopo declarado e foi bloqueada.");
        }

        var resourceUrl = services.GetRequiredService<IOptions<McpSettings>>().Value.ResourceUrl;
        var user = context.User;

        if (user?.Identity?.IsAuthenticated != true || !long.TryParse(user.FindFirst("sub")?.Value, out var userId))
        {
            return Challenge(
                resourceUrl,
                McpChallenge.InvalidToken,
                "Conecte sua conta do Ambrosio",
                required,
                "É preciso conectar sua conta do Ambrosio para usar esta função.");
        }

        var granted = GrantedScopes(user);
        var missing = required.Where(s => !granted.Contains(s)).ToList();

        if (missing.Count > 0)
        {
            var names = missing.Select(DisplayName);

            return Challenge(
                resourceUrl,
                McpChallenge.InsufficientScope,
                $"Falta o escopo {string.Join(' ', missing)}",
                missing,
                $"É preciso autorizar o Ambrosio a: {string.Join("; ", names)}.");
        }

        userContext.UserId = userId;
        userContext.ClientId = user.FindFirst("client_id")?.Value;

        return await next(context, cancellationToken);
    };

    /// <summary>
    /// Escopos do token. O OpenIddict emite <c>scope</c> como texto separado por espaço; um emissor
    /// que mande array chega como vários claims — os dois formatos caem no mesmo conjunto.
    /// </summary>
    public static HashSet<string> GrantedScopes(ClaimsPrincipal user) =>
        user.FindAll("scope")
            .SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .ToHashSet(StringComparer.Ordinal);

    private static string DisplayName(string scope) =>
        ScopeSeeder.Scopes.FirstOrDefault(s => s.Name == scope).DisplayName?.ToLowerInvariant() ?? scope;

    private static CallToolResult Challenge(
        string resourceUrl,
        string error,
        string description,
        IEnumerable<string> scopes,
        string text) =>
        new()
        {
            IsError = true,
            Content = [new TextContentBlock { Text = text }],
            Meta = new JsonObject
            {
                ["mcp/www_authenticate"] = new JsonArray(McpChallenge.Build(resourceUrl, error, description, scopes))
            }
        };

    private static CallToolResult ErrorResult(string text) =>
        new()
        {
            IsError = true,
            Content = [new TextContentBlock { Text = text }]
        };
}
