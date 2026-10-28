using System.Text.Json;
using System.Text.Json.Nodes;
using Ambrosio.McpServer.Mcp.Attributes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Ambrosio.McpServer.Mcp.Filters;

/// <summary>
/// Completa o descritor de cada tool no <c>tools/list</c> (feature 007, T023, R6/R12).
/// </summary>
/// <remarks>
/// Decisão registrada em research.md (R6): o <c>Tool</c> do SDK 2.3.0 aceita <c>_meta</c> mas não tem
/// a propriedade <c>securitySchemes</c>, que a OpenAI lê no nível de cima do descritor. Por isso são
/// dois passos: o filtro de listagem grava <c>_meta.securitySchemes</c> (o espelho que a documentação
/// também aceita) e <c>_meta["ambrosio/endpoint"]</c>, e o filtro de mensagem de saída copia
/// <c>securitySchemes</c> para o nível de cima no JSON que vai ao cliente.
/// </remarks>
public static class ToolDescriptorFilter
{
    public const string EndpointMetaKey = "ambrosio/endpoint";

    public const string SecuritySchemesKey = "securitySchemes";

    public static McpRequestFilter<ListToolsRequestParams, ListToolsResult> Create() => next => async (context, cancellationToken) =>
    {
        var result = await next(context, cancellationToken);
        var collection = context.Server.ServerOptions.ToolCollection;

        if (collection is null)
        {
            return result;
        }

        foreach (var tool in result.Tools)
        {
            if (!collection.TryGetPrimitive(tool.Name, out var serverTool))
            {
                continue;
            }

            tool.Meta ??= new JsonObject();
            tool.Meta[SecuritySchemesKey] = SecuritySchemes(serverTool);

            if (serverTool.Metadata.OfType<AmbrosioEndpointAttribute>().FirstOrDefault() is { } endpoint)
            {
                tool.Meta[EndpointMetaKey] = endpoint.Endpoint;
            }

            if (serverTool.Metadata.OfType<AmbrosioOutputSchemaAttribute>().FirstOrDefault() is { } schema)
            {
                tool.OutputSchema = JsonDocument.Parse(schema.Json).RootElement.Clone();
            }

            // Schema fechado: argumento fora da lista é recusado pelo ScopeChallengeFilter, e o modelo
            // vê isso no descritor (ex.: `iniciar_importacao` não aceita login nem senha).
            if (JsonNode.Parse(tool.InputSchema.GetRawText()) is JsonObject input && !input.ContainsKey("additionalProperties"))
            {
                input["additionalProperties"] = false;
                tool.InputSchema = JsonSerializer.SerializeToElement(input);
            }
        }

        return result;
    };

    /// <summary>
    /// Filtro de saída: promove <c>_meta.securitySchemes</c> ao nível de cima de cada tool da resposta
    /// do <c>tools/list</c>. Mexe só em respostas que têm o array <c>tools</c>.
    /// </summary>
    public static McpMessageFilter CreatePromotion() => next => async (context, cancellationToken) =>
    {
        if (context.JsonRpcMessage is JsonRpcResponse { Result: JsonObject result }
            && result["tools"] is JsonArray tools)
        {
            foreach (var tool in tools.OfType<JsonObject>())
            {
                if (tool["_meta"]?[SecuritySchemesKey] is JsonNode schemes && tool[SecuritySchemesKey] is null)
                {
                    tool[SecuritySchemesKey] = schemes.DeepClone();
                }
            }
        }

        await next(context, cancellationToken);
    };

    private static JsonArray SecuritySchemes(McpServerTool tool)
    {
        if (tool.Metadata.OfType<AmbrosioNoAuthAttribute>().Any())
        {
            return [new JsonObject { ["type"] = "noauth" }];
        }

        var scopes = tool.Metadata.OfType<AmbrosioScopesAttribute>().FirstOrDefault()?.Scopes ?? [];

        return
        [
            new JsonObject
            {
                ["type"] = "oauth2",
                ["scopes"] = new JsonArray(scopes.Select(s => (JsonNode)JsonValue.Create(s)!).ToArray())
            }
        ];
    }
}
