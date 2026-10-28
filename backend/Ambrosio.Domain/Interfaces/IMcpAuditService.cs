using System.Text.Json;
using Ambrosio.Domain.Models;
using Ambrosio.DTO.Enums;

namespace Ambrosio.Domain.Interfaces;

/// <summary>
/// Auditoria das gravações do plugin do ChatGPT (feature 007, FR-020, R16).
/// </summary>
public interface IMcpAuditService
{
    /// <summary>
    /// Grava uma entrada com os argumentos já sanitizados: CPF mascarado, sem senha, sem conteúdo de
    /// arquivo e sem URL assinada. Prévias (<c>confirmar: false</c>) não passam por aqui.
    /// </summary>
    Task RecordAsync(
        long userId,
        string toolName,
        IReadOnlyDictionary<string, JsonElement>? arguments,
        McpAuditOutcome outcome,
        string? clientId,
        string? resultSummary);

    /// <summary>Últimas entradas do próprio usuário (diagnóstico de homologação).</summary>
    Task<IReadOnlyList<McpAuditEntry>> ListRecentAsync(long userId, string? toolName, int limit);
}
