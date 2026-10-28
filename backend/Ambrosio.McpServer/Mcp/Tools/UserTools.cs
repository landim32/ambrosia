using System.ComponentModel;
using Ambrosio.Domain.Interfaces;
using Ambrosio.McpServer.Mcp.Attributes;
using Ambrosio.McpServer.Mcp.Results;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Ambrosio.McpServer.Mcp.Tools;

/// <summary>Usuário atual (feature 007, T052): o <c>GET /users/me</c> da API.</summary>
[McpServerToolType]
public sealed class UserTools
{
    [McpServerTool(Name = "obter_usuario_atual", Title = "Dados da vendedora", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description(
        "Devolve os dados da conta do Ambrosio da vendedora: id, nome, e-mail e forma de login " +
        "(Google ou e-mail e senha). Use quando ela perguntar sobre a própria conta. Não use para " +
        "clientes ou pedidos.")]
    [AmbrosioScopes("customers:read")]
    [AmbrosioEndpoint("GET /users/me")]
    public static Task<CallToolResult> ObterUsuarioAtual(McpUserContext user, IUserService users) =>
        ToolResultFactory.GuardAsync(async () =>
        {
            var me = await users.GetByIdAsync(user.RequireUserId());

            return ToolResultFactory.Success(
                new
                {
                    id = me.Id,
                    nome = me.Name,
                    email = me.Email,
                    provedor = me.Provider.ToString().ToLowerInvariant()
                },
                $"Conta de {me.Name} ({me.Email}).");
        });
}
