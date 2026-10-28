using System.Text.Json;

namespace Ambrosio.McpServer.Mcp;

/// <summary>
/// Quem está chamando a tool, preenchido pelo <c>ScopeChallengeFilter</c> antes da execução
/// (feature 007, T022). Scoped por requisição: as tools recebem este objeto por injeção e nunca
/// aceitam <c>userId</c> como argumento — o usuário é sempre o <c>sub</c> do token do AS.
/// </summary>
public sealed class McpUserContext
{
    /// <summary><c>user_id</c> do token; 0 em tool anônima.</summary>
    public long UserId { get; internal set; }

    /// <summary>Cliente OAuth que obteve o token (vai para a auditoria).</summary>
    public string? ClientId { get; internal set; }

    /// <summary>Nome da tool em execução.</summary>
    public string ToolName { get; internal set; } = string.Empty;

    /// <summary>Argumentos crus da chamada, para a auditoria sanitizar.</summary>
    public IReadOnlyDictionary<string, JsonElement> Arguments { get; internal set; } =
        new Dictionary<string, JsonElement>();

    public bool IsAuthenticated => UserId > 0;

    /// <summary>
    /// Usuário obrigatório. Só falha se uma tool protegida rodar sem passar pelo filtro — erro de
    /// programação, nunca de entrada.
    /// </summary>
    public long RequireUserId() =>
        IsAuthenticated
            ? UserId
            : throw new InvalidOperationException(
                $"A tool '{ToolName}' rodou sem usuário autenticado; confira o [AmbrosioScopes].");
}
