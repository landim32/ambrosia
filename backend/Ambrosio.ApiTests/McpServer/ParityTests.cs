using System.Text.Json;
using Ambrosio.ApiTests.Fixtures;
using Ambrosio.ApiTests.Helpers;
using FluentAssertions;
using Flurl;
using Flurl.Http;

namespace Ambrosio.ApiTests.McpServer;

/// <summary>
/// T033 [US6] (base T070 da 007): paridade entre a API e o plugin (FR-011, R12). Toda operação do
/// Swagger fora de <c>/auth/*</c> precisa de uma tool com <c>_meta["ambrosio/endpoint"]</c> igual, e todo
/// descritor tem título, descrição, anotações e <c>securitySchemes</c>.
/// <para>
/// A feature 008 abriu a primeira exceção a essa regra: as três rotas de credenciais ficam
/// <b>deliberadamente</b> fora do plugin (FR-019), porque credencial do +Vendas só se cadastra na tela de
/// configuração — a lista abaixo é a prova documentada de que a ausência é decisão de contrato, e não
/// esquecimento. Ela é conferida dos dois lados: a operação existe no Swagger, nenhuma tool aponta para
/// ela, e nenhuma tool tem campo de credencial.
/// </para>
/// </summary>
[Collection(ApiTestCollection.API_TESTS)]
public class ParityTests(ApiTestFixture fixture)
{
    /// <summary>
    /// 29 tools do contrato (contracts/mcp-tools.md da 007 + <c>atualizar_base</c> da 008, T034).
    /// </summary>
    private const int ExpectedToolCount = 29;

    /// <summary>
    /// Operações da API sem tool correspondente, por decisão de contrato — <b>FR-019 da 008</b>: as
    /// credenciais do +Vendas (login e senha) só entram pela tela de configuração. Nenhum caminho do
    /// plugin pode escrevê-las ou lê-las, porque a conversa passa pelo ChatGPT e a senha nunca pode sair
    /// da vendedora (FR-003/SC-003). É a única exceção à paridade, e ela é nominal: se uma dessas rotas
    /// ganhar tool, este teste falha.
    /// </summary>
    private static readonly HashSet<string> OperationsWithoutTool = new(StringComparer.Ordinal)
    {
        "GET /imports/mais-vendas/credentials",
        "PUT /imports/mais-vendas/credentials",
        "DELETE /imports/mais-vendas/credentials"
    };

    /// <summary>Sufixo de rota que jamais pode aparecer em <c>_meta["ambrosio/endpoint"]</c>.</summary>
    private const string CredentialsRoute = "/imports/mais-vendas/credentials";

    /// <summary>
    /// Nomes de argumento que nenhuma tool pode ter (FR-018): a credencial não entra na conversa nem por
    /// entrada nem por saída, e a auditoria também os descarta (research R3).
    /// </summary>
    private static readonly string[] CredentialArgumentNames = ["login", "senha", "password"];

    [Fact]
    public async Task EveryApiOperation_HasAToolWithTheSameEndpoint()
    {
        var swagger = await new Url(fixture.BaseUrl).AppendPathSegments("swagger", "v1", "swagger.json")
            .WithTimeout(fixture.TimeoutSeconds).GetStringAsync();

        using var document = JsonDocument.Parse(swagger);
        var operations = document.RootElement.GetProperty("paths").EnumerateObject()
            .Where(path => !path.Name.StartsWith("/auth/", StringComparison.Ordinal))
            .SelectMany(path => path.Value.EnumerateObject()
                .Where(op => op.Name is "get" or "post" or "put" or "delete" or "patch")
                .Select(op => $"{op.Name.ToUpperInvariant()} {path.Name}"))
            .ToHashSet();

        var endpoints = (await Tools())
            .Where(t => t.TryGetProperty("_meta", out var meta) && meta.TryGetProperty("ambrosio/endpoint", out _))
            .Select(t => t.GetProperty("_meta").GetProperty("ambrosio/endpoint").GetString()!)
            .ToHashSet();

        operations.Should().NotBeEmpty();

        // Exceção explícita e nominal (FR-019): o que sobra depois de descontá-la tem de estar coberto.
        operations.Except(OperationsWithoutTool).Except(endpoints)
            .Should().BeEmpty("toda operação da API precisa de uma tool equivalente");
        endpoints.Except(operations).Should().BeEmpty("nenhuma tool pode apontar para uma operação que não existe");

        OperationsWithoutTool.Should().BeSubsetOf(operations,
            "as rotas de credenciais existem na API; a exceção lista operações reais, não fantasmas");
    }

    [Fact]
    public async Task CredentialsRoutes_HaveNoTool()
    {
        // O outro lado da FR-019, declarado em vez de inferido da lista acima: nenhuma tool pode levar a
        // essas rotas. Com uma tool de credencial, a senha passaria a existir dentro da conversa.
        var tools = await Tools();

        var endpoints = tools
            .Where(t => t.TryGetProperty("_meta", out var meta) && meta.TryGetProperty("ambrosio/endpoint", out _))
            .Select(t => t.GetProperty("_meta").GetProperty("ambrosio/endpoint").GetString()!)
            .ToList();

        endpoints.Should().NotContain(e => e.Contains(CredentialsRoute, StringComparison.Ordinal),
            "credencial não tem tool (FR-019)");

        tools.Should().NotContain(t =>
            t.GetProperty("name").GetString()!.Contains("credencia", StringComparison.OrdinalIgnoreCase),
            "nenhuma ferramenta nova de credencial pode entrar no plugin");

        // E os schemas fechados não abrem exceção: nenhum inputSchema tem chave de credencial (FR-018).
        foreach (var tool in tools)
        {
            var name = tool.GetProperty("name").GetString();

            if (tool.GetProperty("inputSchema").TryGetProperty("properties", out var properties))
            {
                properties.EnumerateObject()
                    .Select(p => p.Name)
                    .Intersect(CredentialArgumentNames, StringComparer.OrdinalIgnoreCase)
                    .Should().BeEmpty($"{name} não pode ter campo de credencial (FR-018)");
            }
        }
    }

    [Fact]
    public async Task EveryTool_HasTitleDescriptionAnnotationsAndSecuritySchemes()
    {
        var tools = await Tools();

        tools.Should().HaveCount(ExpectedToolCount);

        foreach (var tool in tools)
        {
            var name = tool.GetProperty("name").GetString();
            tool.GetProperty("title").GetString().Should().NotBeNullOrWhiteSpace(name);
            tool.GetProperty("description").GetString()!.Length.Should().BeGreaterThan(40, name);

            var annotations = tool.GetProperty("annotations");
            foreach (var hint in new[] { "readOnlyHint", "destructiveHint", "openWorldHint" })
            {
                annotations.TryGetProperty(hint, out _).Should().BeTrue($"{name} precisa de {hint}");
            }

            tool.GetProperty("securitySchemes").GetArrayLength().Should().BeGreaterThan(0, name);
            tool.GetProperty("inputSchema").GetProperty("additionalProperties").GetBoolean().Should().BeFalse(name);
        }
    }

    private async Task<List<JsonElement>> Tools()
    {
        var seller = await McpSeller.CreateAsync(fixture);

        return (await seller.Mcp.ListToolsAsync(seller.McpToken)).Result!.Value.GetProperty("tools").EnumerateArray().ToList();
    }
}
