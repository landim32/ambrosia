using System.Text.Json;
using Ambrosio.ApiTests.Fixtures;
using Ambrosio.ApiTests.Helpers;
using FluentAssertions;
using Flurl;
using Flurl.Http;

namespace Ambrosio.ApiTests.McpServer;

/// <summary>
/// T070 [US6]: paridade entre a API e o plugin (FR-011, R12). Toda operação do Swagger fora de
/// <c>/auth/*</c> precisa de uma tool com <c>_meta["ambrosio/endpoint"]</c> igual, e todo descritor
/// tem título, descrição, anotações e <c>securitySchemes</c>.
/// </summary>
[Collection(ApiTestCollection.API_TESTS)]
public class ParityTests(ApiTestFixture fixture)
{
    /// <summary>28 tools do contrato (contracts/mcp-tools.md).</summary>
    private const int ExpectedToolCount = 28;

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
        operations.Except(endpoints).Should().BeEmpty("toda operação da API precisa de uma tool equivalente");
        endpoints.Except(operations).Should().BeEmpty("nenhuma tool pode apontar para uma operação que não existe");
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

    private async Task<List<JsonElement>> Tools() =>
        (await new McpTestClient(fixture).ListToolsAsync()).Result!.Value.GetProperty("tools").EnumerateArray().ToList();
}
