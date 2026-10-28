using System.Text.Json;
using Ambrosio.ApiTests.Fixtures;
using Ambrosio.ApiTests.Helpers;
using FluentAssertions;
using Flurl;
using Flurl.Http;

namespace Ambrosio.ApiTests.McpServer;

/// <summary>
/// T056 [US3]: trilha de auditoria (FR-020, R16), lida pelo diagnóstico de homologação
/// (<c>GET /mcp/diagnostics/audit</c>). Gravação confirmada e recusa por regra aparecem com o
/// desfecho certo e sem CPF completo; prévia não aparece.
/// </summary>
[Collection(ApiTestCollection.API_TESTS)]
public class AuditTests(ApiTestFixture fixture)
{
    [Fact]
    public async Task ConfirmedWriteAndRuleRejection_AreAudited_PreviewIsNot()
    {
        var seller = await McpSeller.CreateAsync(fixture, scopes: WriteToolsTests.WriteScopes);
        var cpf = TestDataHelper.GenerateValidCpf();
        var digits = new string(cpf.Where(char.IsDigit).ToArray());

        await seller.CallAsync("cadastrar_cliente", new { nome = "Rita Auditoria", cpf });
        await seller.CallAsync("cadastrar_cliente", new { nome = "Rita Auditoria", cpf, confirmar = true });
        await seller.CallAsync("cadastrar_cliente", new { nome = "Rita Repetida", cpf, confirmar = true });

        var response = await new Url($"{fixture.McpBaseUrl.TrimEnd('/')}/mcp/diagnostics/audit")
            .SetQueryParam("tool", "cadastrar_cliente")
            .WithOAuthBearerToken(seller.McpToken)
            .WithTimeout(fixture.TimeoutSeconds)
            .AllowAnyHttpStatus()
            .GetAsync();

        response.StatusCode.Should().Be(200);
        var body = await response.GetStringAsync();
        using var json = JsonDocument.Parse(body);
        var entries = json.RootElement.EnumerateArray().ToList();

        entries.Should().HaveCount(2, "a prévia (confirmar: false) não é auditada");
        entries.Select(e => e.GetProperty("outcome").GetString()).Should().BeEquivalentTo(["Recorded", "RejectedByRule"]);
        entries.Should().OnlyContain(e => e.GetProperty("arguments").GetProperty("cpf").GetString()!.Contains("***"));
        body.Should().NotContain(digits);
    }
}
