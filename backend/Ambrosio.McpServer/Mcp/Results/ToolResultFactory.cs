using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ambrosio.Domain.Exceptions;
using FluentValidation;
using ModelContextProtocol.Protocol;

namespace Ambrosio.McpServer.Mcp.Results;

/// <summary>
/// Formato único da saída das tools (feature 007, T024, contracts/mcp-tools.md "Saída"):
/// <c>structuredContent</c> + um item <c>text</c> curto em pt-BR, sempre com <c>consultado_em</c>,
/// listas cortadas em 200 com <c>total</c> e <c>tem_mais</c>, e exceções de domínio como
/// <c>isError</c> com a mesma mensagem da API.
/// </summary>
public static class ToolResultFactory
{
    /// <summary>Teto de linhas de qualquer lista devolvida ao ChatGPT.</summary>
    public const int MaxItems = 200;

    public const string GenericErrorMessage = "Não foi possível concluir a operação no Ambrosio. Tente de novo.";

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Mesmo contrato JSON da API: camelCase e enums como string.</summary>
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    /// <summary>Resultado de sucesso com o objeto como <c>structuredContent</c>.</summary>
    public static CallToolResult Success(object structured, string text)
    {
        var node = JsonSerializer.SerializeToNode(structured, JsonOptions) as JsonObject ?? new JsonObject();
        node["consultado_em"] = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        return new CallToolResult
        {
            StructuredContent = JsonSerializer.SerializeToElement(node),
            Content = [new TextContentBlock { Text = text }]
        };
    }

    /// <summary>
    /// Lista com teto: devolve no máximo <see cref="MaxItems"/> itens, com o <c>total</c> real e
    /// <c>tem_mais</c> quando cortou.
    /// </summary>
    public static CallToolResult List<T>(IEnumerable<T> items, int total, Func<int, int, string> text)
    {
        var page = items.Take(MaxItems).ToList();
        var hasMore = total > page.Count;

        return Success(new { itens = page, total, tem_mais = hasMore }, text(page.Count, total));
    }

    public static CallToolResult Error(string message) =>
        new()
        {
            IsError = true,
            Content = [new TextContentBlock { Text = message }]
        };

    /// <summary>
    /// Converte exceção de domínio no erro que a vendedora entende. Devolve <c>null</c> para exceção
    /// inesperada: quem chama decide logar e usar <see cref="GenericErrorMessage"/>.
    /// </summary>
    public static CallToolResult? FromDomainException(Exception exception) =>
        DomainMessage(exception) is { } message ? Error(message) : null;

    /// <summary>A mensagem que a API devolveria para a mesma exceção, ou <c>null</c> se não for de domínio.</summary>
    public static string? DomainMessage(Exception exception) => exception switch
    {
        ValidationException validation => string.Join(" ", validation.Errors
            .Select(e => e.ErrorMessage)
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Distinct()),
        DuplicateException or NotFoundException or ImageRejectedException
            or ImportLockedException or ImportRunNotCancellableException => exception.Message,
        _ => null
    };

    /// <summary><c>R$ 1.234,56</c> (o espaço inseparável da cultura vira espaço comum).</summary>
    public static string Money(decimal value) => value.ToString("C", PtBr).Replace(' ', ' ');

    public static string Date(DateTime value) => value.ToString("dd/MM/yyyy", PtBr);

    public static string Date(DateOnly value) => value.ToString("dd/MM/yyyy", PtBr);
}
