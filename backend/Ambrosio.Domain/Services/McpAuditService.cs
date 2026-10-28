using System.Text.Json;
using System.Text.Json.Nodes;
using Ambrosio.Domain.Common;
using Ambrosio.Domain.Interfaces;
using Ambrosio.Domain.Models;
using Ambrosio.Domain.ValueObjects;
using Ambrosio.DTO.Enums;
using Ambrosio.Infra.Interfaces.Repository;

namespace Ambrosio.Domain.Services;

/// <summary>
/// Grava <c>mcp_audit_entries</c> (feature 007, R16). A sanitização mora aqui, e não em quem chama,
/// para que nenhuma tool nova consiga esquecer de mascarar o CPF ou de tirar a senha.
/// </summary>
public class McpAuditService : IMcpAuditService
{
    /// <summary>Teto de linhas devolvidas pelo diagnóstico.</summary>
    public const int MaxListLimit = 50;

    private const string Omitted = "[omitido]";

    /// <summary>Chaves removidas da trilha por inteiro: credenciais nunca vão para o banco.</summary>
    private static readonly string[] SecretKeys = ["senha", "password", "pass", "secret", "token", "login"];

    /// <summary>Chaves com conteúdo de arquivo (bytes, base64): ficam só como marcador.</summary>
    private static readonly string[] FileContentKeys = ["conteudo", "content", "base64", "bytes", "data"];

    private readonly IMcpAuditRepository<McpAuditEntry> _repository;
    private readonly IClock _clock;

    public McpAuditService(IMcpAuditRepository<McpAuditEntry> repository, IClock clock)
    {
        _repository = repository;
        _clock = clock;
    }

    public async Task RecordAsync(
        long userId,
        string toolName,
        IReadOnlyDictionary<string, JsonElement>? arguments,
        McpAuditOutcome outcome,
        string? clientId,
        string? resultSummary)
    {
        var sanitized = new JsonObject();

        foreach (var (key, value) in arguments ?? new Dictionary<string, JsonElement>())
        {
            if (!IsSecret(key))
            {
                sanitized[key] = Sanitize(key, JsonNode.Parse(value.GetRawText()));
            }
        }

        var summary = resultSummary is { Length: > 500 } ? resultSummary[..500] : resultSummary;

        var entry = McpAuditEntry.Create(
            userId,
            toolName,
            sanitized.ToJsonString(),
            outcome,
            clientId,
            summary,
            _clock.UtcNow);

        await _repository.InsertAsync(entry);
    }

    public async Task<IReadOnlyList<McpAuditEntry>> ListRecentAsync(long userId, string? toolName, int limit) =>
        await _repository.ListRecentAsync(userId, toolName, Math.Clamp(limit, 1, MaxListLimit));

    /// <summary>
    /// Percorre o valor e aplica as regras pela chave: segredo some, CPF vira máscara, conteúdo de
    /// arquivo vira marcador e URL perde a query (é nela que mora a assinatura de uma pré-assinada).
    /// </summary>
    private static JsonNode? Sanitize(string key, JsonNode? node)
    {
        if (node is null || IsSecret(key))
        {
            return null;
        }

        if (FileContentKeys.Any(k => key.Equals(k, StringComparison.OrdinalIgnoreCase)))
        {
            return JsonValue.Create(Omitted);
        }

        switch (node)
        {
            case JsonObject obj:
                var copy = new JsonObject();
                foreach (var (childKey, childValue) in obj)
                {
                    if (IsSecret(childKey))
                    {
                        continue;
                    }

                    copy[childKey] = Sanitize(childKey, childValue?.DeepClone());
                }

                return copy;

            case JsonArray array:
                var items = new JsonArray();
                foreach (var item in array)
                {
                    items.Add(Sanitize(key, item?.DeepClone()));
                }

                return items;

            case JsonValue value when value.TryGetValue<string>(out var text):
                if (key.Contains("cpf", StringComparison.OrdinalIgnoreCase))
                {
                    return JsonValue.Create(Cpf.TryParse(text, out var cpf) ? cpf!.Masked : Omitted);
                }

                return JsonValue.Create(StripUrlQuery(text));

            default:
                return node;
        }
    }

    private static bool IsSecret(string key) =>
        SecretKeys.Any(k => key.Equals(k, StringComparison.OrdinalIgnoreCase));

    private static string StripUrlQuery(string text)
    {
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrEmpty(uri.Query))
        {
            return text;
        }

        return uri.GetLeftPart(UriPartial.Path);
    }
}
