using System.Threading.RateLimiting;
using Ambrosio.DTO.Settings;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Ambrosio.McpServer.Middlewares;

/// <summary>
/// Checagem de origem e limite de requisições do endpoint MCP (feature 007, T021, R17).
/// </summary>
/// <remarks>
/// O transporte do MCP é HTTP e aceita POST de qualquer lugar. Chamadas do ChatGPT são
/// servidor-a-servidor e <b>não</b> mandam header <c>Origin</c>; requisição de navegador manda. Sem
/// esta checagem, uma página maliciosa aberta no navegador da vendedora poderia conversar com o
/// endpoint no mesmo host (a CSP que a OpenAI exige de quem publica plugin é a outra metade dessa
/// proteção, e não nos protege aqui).
/// <para>
/// Vazia a lista significa "nenhuma origem de navegador", que é o valor correto em produção: o único
/// cliente é o ChatGPT.
/// </para>
/// </remarks>
public sealed class OriginValidationMiddleware(
    RequestDelegate next,
    IOptions<McpSettings> settings,
    ILogger<OriginValidationMiddleware> logger)
{
    /// <summary>Mensagem curta e fixa: o 403 não explica quais origens são aceitas.</summary>
    public const string ForbiddenMessage = "Origem não permitida.";

    public async Task InvokeAsync(HttpContext context)
    {
        // Header ausente = chamada servidor-a-servidor (o caso do ChatGPT): passa.
        if (!context.Request.Headers.TryGetValue("Origin", out var origin)
            || StringValues.IsNullOrEmpty(origin))
        {
            await next(context);
            return;
        }

        var allowed = settings.Value.AllowedOrigins;

        // Comparação exata de origem (scheme + host + porta), sem wildcard: "https://ambrosiaai.site"
        // não casa com "https://ambrosiaai.site.evil.com".
        if (allowed.Any(a => string.Equals(a, origin.ToString(), StringComparison.Ordinal)))
        {
            await next(context);
            return;
        }

        logger.LogInformation(
            "Requisição MCP de origem não permitida ({Origin}) ao {Path} recusada com 403.",
            origin.ToString(), context.Request.Path);

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsync(ForbiddenMessage);
    }
}

/// <summary>
/// Limitador de requisições ao MCP por <c>sub</c>, com bucket por chave (feature 007, R17).
/// </summary>
/// <remarks>
/// A OpenAI exige que qualquer endpoint de plugin tenha rate limit. A chave é o usuário autenticado,
/// nunca o IP: atrás do gateway o IP de origem é o do proxy (ou do cluster de conectores da OpenAI),
/// então limitar por IP puniria todas as vendedoras por causa de uma. Chamada anônima cai num balde
/// separado, o que também cobre o flood pré-autenticação.
/// </remarks>
public static class McpRateLimiter
{
    /// <summary>Nome da política registrada.</summary>
    public const string PolicyName = "mcp";

    /// <summary>Requisições por janela — folgado para uso humano de conversa, apertado para script.</summary>
    public const int PermitPerWindow = 120;

    /// <summary>Janela do bucket.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    public static IServiceCollection AddAmbrosioMcpRateLimiter(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy<string>(PolicyName, httpContext =>
            {
                // A chave do bucket é o usuário autenticado; anônimo tem balde próprio para cobrir o
                // flood pré-autenticação sem punir quem já está logado.
                var userId = httpContext.User?.FindFirst("sub")?.Value;
                var key = string.IsNullOrEmpty(userId) ? "anonymous" : userId;

                return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = PermitPerWindow,
                    Window = Window,
                    QueueLimit = 0
                });
            });
        });

        return services;
    }

    /// <summary>
    /// Coloca o <see cref="OriginValidationMiddleware"/> na esteira, antes da autenticação: uma origem
    /// de navegador proibida é recusada antes de qualquer chave ser conferida, e o 403 nunca vaza
    /// informação sobre o token.
    /// </summary>
    public static IApplicationBuilder UseAmbrosioMcpOriginGuard(this IApplicationBuilder app)
        => app.UseMiddleware<OriginValidationMiddleware>();
}
