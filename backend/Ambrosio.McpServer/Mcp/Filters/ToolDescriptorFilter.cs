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

    /// <summary>Chave que o ChatGPT lê para saber quais argumentos recebem arquivos da conversa.</summary>
    public const string FileParamsMetaKey = "openai/fileParams";

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

            // Ferramentas que recebem anexo da conversa (feature 009, T057): sem esta linha o ChatGPT
            // não oferece o arquivo como objeto de download e a tool recebe nada.
            if (serverTool.Metadata.OfType<AmbrosioFileParamsAttribute>().FirstOrDefault() is { } fileParams)
            {
                tool.Meta[FileParamsMetaKey] = new JsonArray(
                    fileParams.FileParams.Select(name => (JsonNode)JsonValue.Create(name)!).ToArray());

                tool.InputSchema = WithOpenAiFileSchema(tool.InputSchema, fileParams.FileParams);
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

    /// <summary>
    /// Troca o schema de cada campo de arquivo pelo formato que o ChatGPT exige para entregar o anexo
    /// (Apps SDK, <c>openai/fileParams</c>): objeto com <c>download_url</c> e <c>file_id</c>
    /// obrigatórios e <c>mime_type</c>/<c>file_name</c> declarados como opcionais — num array, o mesmo
    /// objeto em <c>items</c>. O schema que o SDK gera do <c>FileArgument</c> não marca nada como
    /// obrigatório (as propriedades são anuláveis para a tool responder com a mensagem da 004 em vez de
    /// falhar na desserialização), e fora desse formato o ChatGPT não manda o arquivo: o modelo fica
    /// sem link e desiste. A desserialização continua tolerante — só o descritor muda.
    /// </summary>
    private static JsonElement WithOpenAiFileSchema(JsonElement inputSchema, IReadOnlyList<string> fileParams)
    {
        if (JsonNode.Parse(inputSchema.GetRawText()) is not JsonObject input
            || input["properties"] is not JsonObject properties)
        {
            return inputSchema;
        }

        foreach (var name in fileParams)
        {
            if (properties[name] is not JsonObject current)
            {
                continue;
            }

            var isArray = current["type"] is JsonValue type && type.GetValue<string>() == "array"
                          || current["type"] is JsonArray types && types.Any(t => t?.GetValue<string>() == "array");

            JsonObject replacement = isArray
                ? new JsonObject { ["type"] = "array", ["items"] = OpenAiFileObject() }
                : OpenAiFileObject();

            if (current["description"] is JsonNode description)
            {
                replacement["description"] = description.DeepClone();
            }

            properties[name] = replacement;
        }

        return JsonSerializer.SerializeToElement(input);
    }

    private static JsonObject OpenAiFileObject() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["download_url"] = new JsonObject { ["type"] = "string", ["description"] = "URL https de onde o arquivo é baixado." },
            ["file_id"] = new JsonObject { ["type"] = "string", ["description"] = "Id do arquivo no ChatGPT." },
            ["mime_type"] = new JsonObject { ["type"] = "string", ["description"] = "Tipo do arquivo (ex.: image/jpeg)." },
            ["file_name"] = new JsonObject { ["type"] = "string", ["description"] = "Nome do arquivo (ex.: foto.jpg)." }
        },
        ["required"] = new JsonArray("download_url", "file_id")
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
