using System.ComponentModel;
using System.Text.Json;
using Ambrosio.Domain.Interfaces;
using Ambrosio.McpServer.Mcp.Attributes;
using Ambrosio.McpServer.Mcp.Results;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Ambrosio.McpServer.Mcp.Tools;

/// <summary>
/// Tools do próprio plugin (feature 007, T045, contracts/mcp-tools.md "Tools do plugin"): a tool de
/// perfil, que o ChatGPT usa para reconhecer a conta conectada, e a tool anônima de apresentação.
/// </summary>
[McpServerToolType]
public sealed class ProfileTools
{
    /// <summary><c>outputSchema</c> exato do contrato: <c>id</c> obrigatório e não vazio.</summary>
    private const string ProfileOutputSchema = """
        {
          "type": "object",
          "properties": {
            "id": { "type": "string", "minLength": 1, "pattern": "\\S" },
            "name": { "type": "string" },
            "email": { "type": "string" }
          },
          "required": ["id"],
          "additionalProperties": false
        }
        """;

    private const string AboutText =
        "O Ambrosio é o assistente de vendas da consultora: guarda clientes, pedidos, devoluções e " +
        "cancelamentos, importa as vendas do +Vendas e responde perguntas como \"quem não compra há 60 " +
        "dias?\" ou \"quanto vendi neste mês de cota?\". Para consultar ou gravar dados, conecte a sua " +
        "conta do Ambrosio quando o ChatGPT pedir: o login é feito na tela do próprio Ambrosio, com " +
        "e-mail e senha ou com o Google, e você escolhe o que autorizar. A senha do +Vendas nunca é " +
        "pedida na conversa.";

    [McpServerTool(
        Name = "perfil_ambrosio",
        Title = "Conta do Ambrosio conectada",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description(
        "Identifica a conta do Ambrosio conectada a esta conversa (id, nome e e-mail). Use para saber " +
        "quem é a vendedora antes de responder sobre os dados dela. Não use para consultar clientes ou " +
        "pedidos.")]
    [McpMeta("openai/profile", true)]
    [AmbrosioScopes("openid")]
    [AmbrosioOutputSchema(ProfileOutputSchema)]
    public static async Task<CallToolResult> PerfilAmbrosio(McpUserContext user, IUserService users)
    {
        var profile = await users.GetByIdAsync(user.RequireUserId());

        var structured = new Dictionary<string, string>
        {
            ["id"] = profile.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["name"] = profile.Name,
            ["email"] = profile.Email
        };

        // O schema da tool de perfil é fechado (additionalProperties: false), então este resultado não
        // passa pela fábrica — que acrescentaria `consultado_em`. O texto é o próprio JSON, como pede o
        // contrato.
        var json = JsonSerializer.Serialize(structured);

        return new CallToolResult
        {
            StructuredContent = JsonSerializer.SerializeToElement(structured),
            Content = [new TextContentBlock { Text = json }]
        };
    }

    [McpServerTool(
        Name = "sobre_ambrosio",
        Title = "O que é o Ambrosio",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description(
        "Explica o que o Ambrosio faz e como usar o plugin. Use quando a pessoa perguntar o que é o " +
        "Ambrosio ou como começar. Não acessa dado nenhum.")]
    [AmbrosioScopes("openid")]
    public static CallToolResult SobreAmbrosio() =>
        ToolResultFactory.Success(
            new
            {
                sobre = AboutText,
                site = "https://ambrosioai.site"
            },
            AboutText);
}
