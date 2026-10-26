using System.Text.Json;
using Ambrosia.ApiTests.Fixtures;
using Ambrosia.ApiTests.Helpers;
using Ambrosia.DTO.Common;
using Ambrosia.DTO.Customers;
using FluentAssertions;

namespace Ambrosia.ApiTests.Controllers;

/// <summary>
/// T057 [US2]: <c>/customers</c> ponta a ponta (quickstart, Cenário 2) — cadastro com CPF
/// protegido, duplicidade 409, busca paginada com CPF mascarado (SC-005), detalhe com CPF
/// completo e resumo zerado, e atualização de nome.
/// </summary>
[Collection(ApiTestCollection.API_TESTS)]
public class CustomersControllerTests
{
    private const string CUSTOMERS = "/customers";

    private readonly ApiTestFixture _fixture;

    public CustomersControllerTests(ApiTestFixture fixture) => _fixture = fixture;

    private string Token => _fixture.TokenA;

    private async Task<CustomerDetailInfo> CreateCustomerAsync(string? name = null, string? cpf = null)
    {
        var payload = new CustomerInsertInfo
        {
            Name = name ?? TestDataHelper.NewCustomer().Name,
            Cpf = cpf ?? TestDataHelper.GenerateValidCpf()
        };

        var response = await _fixture.PostAsync(CUSTOMERS, Token, payload);

        response.StatusCode.Should().Be(201, $"o cadastro deveria funcionar: {response}");

        return response.As<CustomerDetailInfo>();
    }

    [Fact]
    public async Task Create_ValidCpf_ShouldReturn201WithLocation()
    {
        // Arrange (FR-006)
        var cpf = TestDataHelper.GenerateValidCpf();
        var info = new CustomerInsertInfo { Name = "Maria Silva", Cpf = cpf };

        // Act
        var response = await _fixture.PostAsync(CUSTOMERS, Token, info);

        // Assert: 201 + Location /customers/{id} (contrato openapi.yaml).
        response.StatusCode.Should().Be(201);

        var created = response.As<CustomerDetailInfo>();

        response.Location.Should().NotBeNullOrWhiteSpace();
        response.Location.Should().EndWith($"/{created.Id}");
        created.Id.Should().BeGreaterThan(0);
        created.Name.Should().Be("Maria Silva");
        created.CreatedAt.Should().NotBe(default);
    }

    [Fact]
    public async Task Create_InvalidCpf_ShouldReturn400WithCampoCpf()
    {
        // Act: FR-007 — dígitos verificadores inválidos.
        var response = await _fixture.PostAsync(CUSTOMERS, Token,
            new CustomerInsertInfo { Name = "Maria Silva", Cpf = "111.111.111-11" });

        // Assert: FR-024 — ErrorResult em português apontando o campo.
        response.StatusCode.Should().Be(400);

        using var document = JsonDocument.Parse(response.Body);
        var root = document.RootElement;

        root.GetProperty("sucesso").GetBoolean().Should().BeFalse();
        root.GetProperty("mensagem").GetString().Should().Be("Dados inválidos.");

        var cpfError = root.GetProperty("erros").EnumerateArray()
            .Single(e => e.GetProperty("campo").GetString() == "cpf");

        cpfError.GetProperty("mensagem").GetString().Should().Be("CPF inválido.");
    }

    [Fact]
    public async Task Create_DuplicateCpfSameUser_ShouldReturn409()
    {
        // Arrange: FR-010 — o mesmo CPF, escrito sem pontuação na segunda chamada.
        var first = await CreateCustomerAsync(name: "Duplicada", cpf: TestDataHelper.QUICKSTART_CPF);

        // Act
        var second = await _fixture.PostAsync(CUSTOMERS, Token,
            new CustomerInsertInfo
            {
                Name = "Outra Pessoa",
                Cpf = TestDataHelper.QUICKSTART_CPF_DIGITS
            });

        // Assert
        second.StatusCode.Should().Be(409);

        using var document = JsonDocument.Parse(second.Body);
        document.RootElement.GetProperty("sucesso").GetBoolean().Should().BeFalse();
        document.RootElement.GetProperty("mensagem").GetString()
            .Should().Be("Já existe um cliente com este CPF.");

        // O primeiro registro permanece intacto.
        (await _fixture.GetAsync($"{CUSTOMERS}/{first.Id}", Token)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task List_WithSearch_ShouldReturnOnlyMatchesWithMaskedCpf()
    {
        // Arrange: SC-005 — três clientes com um token de busca comum e um que não casa.
        var tag = Guid.NewGuid().ToString("N")[..8];

        var alfaCpf = TestDataHelper.GenerateValidCpf();
        var betaCpf = TestDataHelper.GenerateValidCpf();
        var gamaCpf = TestDataHelper.GenerateValidCpf();

        await CreateCustomerAsync(name: $"{tag} Alfa", cpf: alfaCpf);
        await CreateCustomerAsync(name: $"{tag} Beta", cpf: betaCpf);
        await CreateCustomerAsync(name: $"{tag} Gama", cpf: gamaCpf);

        // Act: busca por token (case-insensitive), tamanho de página pequeno.
        var response = await _fixture.GetAsync(
            $"{CUSTOMERS}?search={tag}&page=1&pageSize=2", Token);

        // Assert: a busca casa os três; a página 2 devolve o terceiro.
        response.StatusCode.Should().Be(200);

        var firstPage = response.As<PagedResult<CustomerInfo>>();
        firstPage.TotalCount.Should().Be(3);
        firstPage.Items.Should().HaveCount(2);
        firstPage.Page.Should().Be(1);
        firstPage.PageSize.Should().Be(2);

        var secondPage = (await _fixture.GetAsync(
            $"{CUSTOMERS}?search={tag}&page=2&pageSize=2", Token)).As<PagedResult<CustomerInfo>>();
        secondPage.Items.Should().ContainSingle();

        // SC-005: apenas a máscara aparece, e o corpo nunca contém um CPF completo.
        foreach (var item in firstPage.Items.Concat(secondPage.Items))
        {
            item.CpfMasked.Should().MatchRegex(@"^\*\*\*\.\d{3}\.\d{3}-\*\*$");
        }

        response.Body.Should().NotContain(alfaCpf);
        response.Body.Should().NotContain(alfaCpf.Replace(".", string.Empty).Replace("-", string.Empty));
        response.Body.Should().NotContain(betaCpf);
        response.Body.Should().NotContain(gamaCpf);

        // Nenhum objeto de listagem tem propriedade "cpf".
        var items = JsonSerializer.Deserialize<JsonElement>(response.Body).GetProperty("items");
        items.EnumerateArray().Select(e => e.TryGetProperty("cpf", out _)).Should().OnlyContain(x => x == false);
    }

    [Fact]
    public async Task List_PageBeyondEnd_ShouldReturnEmptyItems()
    {
        // Arrange: FR-008 — página além do fim devolve items vazio com totalCount correto.
        await CreateCustomerAsync();

        // Act
        var response = await _fixture.GetAsync($"{CUSTOMERS}?page=9999&pageSize=10", Token);

        // Assert
        response.StatusCode.Should().Be(200);

        var page = response.As<PagedResult<CustomerInfo>>();
        page.Items.Should().BeEmpty();
        page.Page.Should().Be(9999);
        page.TotalCount.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task GetById_ShouldReturnFullCpfAndZeroSummary()
    {
        // Arrange: FR-009 + Cenário 2 do quickstart (detalhe com resumo zerado).
        var cpf = TestDataHelper.GenerateValidCpf();
        var created = await CreateCustomerAsync(name: "Detalhe Completo", cpf: cpf);

        // Act
        var response = await _fixture.GetAsync($"{CUSTOMERS}/{created.Id}", Token);

        // Assert
        response.StatusCode.Should().Be(200);

        var detail = response.As<CustomerDetailInfo>();
        detail.Id.Should().Be(created.Id);
        detail.Cpf.Should().Be(cpf, "o detalhe decifra e formata o CPF");
        detail.CpfMasked.Should().NotBeNullOrWhiteSpace();
        detail.Summary.Purchases.Should().Be(0);
        detail.Summary.NetTotal.Should().Be(0);
        detail.Summary.LastPurchase.Should().BeNull();
        detail.Summary.DaysSinceLastPurchase.Should().BeNull();
    }

    [Fact]
    public async Task GetById_NonexistentId_ShouldReturn404WithNotFoundShape()
    {
        // Act
        var response = await _fixture.GetAsync($"{CUSTOMERS}/{long.MaxValue}", Token);

        // Assert: FR-022/FR-024 — a mesma mensagem usada para "é de outro usuário".
        response.StatusCode.Should().Be(404);

        using var document = JsonDocument.Parse(response.Body);
        document.RootElement.GetProperty("sucesso").GetBoolean().Should().BeFalse();
        document.RootElement.GetProperty("mensagem").GetString().Should().Be("Cliente não encontrado.");
        document.RootElement.GetProperty("erros").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Update_Name_ShouldReturn200()
    {
        // Arrange: FR-009.
        var created = await CreateCustomerAsync(name: "Nome Antigo");

        // Act
        var response = await _fixture.PutAsync($"{CUSTOMERS}/{created.Id}", Token,
            new CustomerUpdateInfo { Name = "Nome Novo" });

        // Assert
        response.StatusCode.Should().Be(200);
        response.As<CustomerDetailInfo>().Name.Should().Be("Nome Novo");

        // A leitura posterior confirma a persistência.
        var reread = await _fixture.GetAsync($"{CUSTOMERS}/{created.Id}", Token);
        reread.As<CustomerDetailInfo>().Name.Should().Be("Nome Novo");
    }

    [Fact]
    public async Task Update_InvalidName_ShouldReturn400WithCampoName()
    {
        // Arrange
        var created = await CreateCustomerAsync(name: "Nome Correto");

        // Act: menos de 2 caracteres.
        var response = await _fixture.PutAsync($"{CUSTOMERS}/{created.Id}", Token,
            new CustomerUpdateInfo { Name = "A" });

        // Assert
        response.StatusCode.Should().Be(400);
        response.Body.Should().Contain("name");
        response.Body.Should().Contain("entre 2 e 200");
    }

    [Fact]
    public async Task List_WithInvalidPageSize_ShouldReturn400()
    {
        // Act: FR-008/research §9 — pageSize acima de 100 é recusado.
        var response = await _fixture.GetAsync($"{CUSTOMERS}?page=1&pageSize=500", Token);

        // Assert
        response.StatusCode.Should().Be(400);
        response.Body.Should().Contain("pageSize");
    }
}
