using System.Security.Cryptography;
using Ambrosio.ApiTests.Fixtures;
using Ambrosio.DTO.Customers;
using Ambrosio.DTO.Enums;
using Ambrosio.DTO.Imports;
using Ambrosio.DTO.Orders;
using FluentAssertions;

namespace Ambrosio.ApiTests.Helpers;

/// <summary>
/// Geração de dados de teste (skill <c>dotnet-test-api</c>): CPFs válidos aleatórios e payloads
/// de cliente/pedido. Cada execução cria documentos e números novos, então os testes não brigam
/// com dados de execuções anteriores no banco de desenvolvimento.
/// </summary>
internal static class TestDataHelper
{
    /// <summary>CPF de referência do quickstart (válido), com pontuação.</summary>
    public const string QUICKSTART_CPF = "529.982.247-25";

    /// <summary>
    /// O mesmo documento sem pontuação: a unicidade é por CPF normalizado (FR-010), então cadastrar
    /// esta forma sobre <see cref="QUICKSTART_CPF"/> deve dar 409.
    /// </summary>
    public const string QUICKSTART_CPF_DIGITS = "52998224725";

    /// <summary>Total da venda de exemplo do doc 01 / Cenário 3 do quickstart.</summary>
    public const decimal SALE_TOTAL = 589.80m;

    /// <summary>Data da venda de exemplo (ontem em relação ao "hoje" do quickstart).</summary>
    public static readonly DateOnly SaleDate = new(2026, 10, 24);

    /// <summary>
    /// CPF brasileiro aleatório e matematicamente válido: 9 dígitos fortuitos + 2 verificadores
    /// calculados por módulo 11, formatado como <c>000.000.000-00</c>.
    /// </summary>
    public static string GenerateValidCpf()
    {
        var digits = new int[11];

        // Sequências repetidas (111.111.111-11) são rejeitadas pela regra de negócio (research §5):
        // sorteia de novo até ter pelo menos dois dígitos diferentes.
        do
        {
            for (var i = 0; i < 9; i++)
            {
                digits[i] = RandomNumberGenerator.GetInt32(0, 10);
            }
        }
        while (digits[..9].Distinct().Count() < 2);

        digits[9] = ComputeFirstCheckDigit(digits);
        digits[10] = ComputeSecondCheckDigit(digits);

        var raw = new string(digits.Select(d => (char)('0' + d)).ToArray());

        return $"{raw[..3]}.{raw[3..6]}.{raw[6..9]}-{raw[9..]}";
    }

    /// <summary>Cliente inédito para <c>POST /customers</c>.</summary>
    public static CustomerInsertInfo NewCustomer(string? name = null) => new()
    {
        Name = name ?? $"Cliente {Guid.NewGuid().ToString("N")[..8]}",
        Cpf = GenerateValidCpf()
    };

    /// <summary>
    /// Venda do quickstart: R$ 589,80 com "Calça Inez" (42CA500UIE_12182, tam. 38, R$ 399,90) e
    /// "Blusa Lia" (41BL200AAA_00001, tam. P, R$ 189,90), canal Linx, origem cota, status
    /// "Faturado" e número único por execução (T075).
    /// </summary>
    public static OrderInsertInfo NewSaleOrder(long customerId, string? orderNumber = null) => new()
    {
        CustomerId = customerId,
        OrderNumber = orderNumber ?? NewOrderNumber(),
        OrderDate = SaleDate,
        Kind = OrderKind.Sale,
        Channel = "Linx",
        Origin = OrderOrigin.Cota,
        Status = "Faturado",
        Total = SALE_TOTAL,
        Items =
        [
            new OrderItemInsertInfo
            {
                ProductName = "Calça Inez",
                SkuRef = "42CA500UIE_12182",
                Size = "38",
                Price = 399.90m
            },
            new OrderItemInsertInfo
            {
                ProductName = "Blusa Lia",
                SkuRef = "41BL200AAA_00001",
                Size = "P",
                Price = 189.90m
            }
        ]
    };

    /// <summary>
    /// Cancelamento do quickstart: total negativo, mesma data da venda original e item com preço
    /// negativo (SC-006 — nada de informação é perdido).
    /// </summary>
    /// <param name="customerId">Cliente dono do pedido cancelado.</param>
    /// <param name="orderNumber">Número do pedido vendido que se cancela (a unicidade é por
    /// número + data + tipo, então repetir o número com kind diferente é justamente o caso).</param>
    public static OrderInsertInfo NewCancellationOrder(long customerId, string orderNumber) => new()
    {
        CustomerId = customerId,
        OrderNumber = orderNumber,
        OrderDate = DateOnly.FromDateTime(DateTime.UtcNow),
        Kind = OrderKind.Cancellation,
        Channel = "Linx",
        Status = "Cancelado",
        Total = -SALE_TOTAL,
        OriginalSaleOn = SaleDate,
        CancelledOn = DateOnly.FromDateTime(DateTime.UtcNow),
        Items =
        [
            new OrderItemInsertInfo
            {
                ProductName = "Calça Inez",
                SkuRef = "42CA500UIE_12182",
                Size = "38",
                Price = -399.90m
            }
        ]
    };

    /// <summary>Número de pedido inédito no formato do +Vendas (<c>v1161148crb-01</c>).</summary>
    public static string NewOrderNumber() =>
        $"v{Guid.NewGuid().ToString("N")[..10].ToLowerInvariant()}-01";

    // ---------------------------------------------------------------------------
    // Importação do +Vendas (feature 006, T035; credenciais guardadas pela feature 008, T020)
    //
    // Nada aqui usa valor real de login/senha do +Vendas: os identificadores são os
    // cenários do coletor Fake da homologação (research §3) — `conta+cenario` e a senha
    // como versão do conteúdo. Um login real nunca entraria em teste, Bruno ou doc.
    //
    // Desde a feature 008 o `POST /runs` não transporta credencial nenhuma (FR-001/FR-002): o que
    // escolhe o cenário do Fake é o cadastro gravado pelo `PUT /credentials`, que o worker decifra
    // na execução (research R3). Por isso os inícios desta classe CADASTRAM antes e POSTAM só o
    // período — o mesmo caminho da tela.
    // ---------------------------------------------------------------------------

    /// <summary>Rota do início de importação (feature 006 FR-003; corpo reduzido pela 008).</summary>
    public const string IMPORT_RUNS_PATH = "/imports/mais-vendas/runs";

    /// <summary>Rota do "Atualizar base" (feature 008, FR-011/FR-013): período calculado no servidor.</summary>
    public const string UPDATE_BASE_PATH = "/imports/mais-vendas/runs/update-base";

    /// <summary>Conta de referência do cenário bom (o identificador antes do `+` vira o vínculo).</summary>
    public const string FAKE_ACCOUNT = "s482";

    /// <summary>Cenário bom do fake: 49 pedidos entre 26/09 e 25/10/2026 somando R$ 123.300,58.</summary>
    public const string SCENARIO_OK = "ok";

    /// <summary>Período do cenário bom (quickstart §4 item 2).</summary>
    public static readonly DateOnly ImportStart = new(2026, 9, 26);

    /// <summary>Fim do período do cenário bom.</summary>
    public static readonly DateOnly ImportEnd = new(2026, 10, 25);

    /// <summary>Total líquido esperado pelo cenário bom (SC-001 em homologação).</summary>
    public const decimal IMPORT_NET_TOTAL = 123300.58m;

    /// <summary>Quantidade de registros do cenário bom.</summary>
    public const int IMPORT_ORDER_COUNT = 49;

    /// <summary>
    /// Login do fake no formato <c>conta+cenario</c>. Com <paramref name="account"/> nulo usa a conta de
    /// referência; passar outra (ex.: <c>s999</c>) é o caso "conta diferente da vinculada" (FR-008b).
    /// </summary>
    public static string FakeLogin(string scenario, string? account = null) =>
        $"{account ?? FAKE_ACCOUNT}+{scenario}";

    /// <summary>
    /// Senha de teste identificável, usada pela asserção de que credencial não aparece em resposta
    /// alguma nem no banco (SC-004). É uma string de teste, não uma senha de ninguém.
    /// </summary>
    public const string TRACEABLE_PASSWORD = "SENHA-TESTE-XYZ123";

    /// <summary>
    /// Enfileira uma importação e devolve o resumo do <c>202</c> (atalho de T035/T020). O teste escolhe
    /// cenário, versão (senha), período e se é simulação.
    /// <para>
    /// <b>Passo 1 — cadastra; passo 2 — inicia</b>: <c>login</c>/<c>password</c> aqui são o que o teste
    /// quer gravado como credencial do +Vendas (o cenário do Fake é lido da linha cifrada, FR-001 da
    /// 008), nunca o corpo do <c>POST /runs</c>. Passar os dois nulos/vazios deixa a vendedora sem
    /// cadastro, que é exatamente o caso "início recusado" (T020/US2-1).
    /// </para>
    /// </summary>
    /// <exception cref="InvalidOperationException">Quando o servidor não respondeu 202 — com corpo.</exception>
    public static async Task<ImportRunInfo> StartImportAsync(
        this ApiTestFixture fixture,
        string token,
        string? login,
        string? password,
        DateOnly start,
        DateOnly end,
        bool dryRun = false)
    {
        await fixture.SaveScenarioCredentialsAsync(token, login, password);

        var response = await fixture.PostAsync(IMPORT_RUNS_PATH, token, PeriodPayload(start, end, dryRun));

        if (response.StatusCode != 202)
        {
            throw new InvalidOperationException(
                $"POST {IMPORT_RUNS_PATH} respondeu {response.StatusCode} (esperado 202): {response.Body}");
        }

        return response.As<ImportRunInfo>();
    }

    /// <summary>
    /// Enfileira a importação do cenário bom devolvendo também o corpo bruto — para os testes que
    /// precisam asserir sobre o texto da resposta (ex.: nenhuma ocorrência das credenciais, SC-004).
    /// </summary>
    public static async Task<HttpCallResult> StartImportRawAsync(
        this ApiTestFixture fixture,
        string token,
        string? login,
        string? password,
        DateOnly start,
        DateOnly end,
        bool dryRun = false)
    {
        await fixture.SaveScenarioCredentialsAsync(token, login, password);

        return await fixture.PostAsync(IMPORT_RUNS_PATH, token, PeriodPayload(start, end, dryRun));
    }

    /// <summary>
    /// POST <c>/runs</c> no <b>formato antigo</b> (feature 006), com <c>login</c> e <c>password</c> no
    /// corpo, sem cadastrar nada antes (T020). Serve ao caso "cliente ainda manda credencial": o binder
    /// descarta o que <see cref="ImportRunStartInfo"/> não tem, então a requisição não é recusada por
    /// campo desconhecido — ela cai na checagem de credenciais, e o valor enviado nunca é gravado nem
    /// ecoado. O helper manda o corpo cru de propósito: é a única forma de provar o ignorado.
    /// </summary>
    public static async Task<HttpCallResult> StartImportWithLegacyBodyRawAsync(
        this ApiTestFixture fixture,
        string token,
        string login,
        string password,
        DateOnly start,
        DateOnly end,
        bool dryRun = false)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        return await fixture.PostAsync(IMPORT_RUNS_PATH, token, new Dictionary<string, object?>
        {
            ["login"] = login,
            ["password"] = password,
            ["startDate"] = start.ToString("yyyy-MM-dd"),
            ["endDate"] = end.ToString("yyyy-MM-dd"),
            ["dryRun"] = dryRun
        });
    }

    /// <summary>
    /// <c>POST /runs/update-base</c> exigindo <c>202</c> (feature 008, FR-011/FR-013, T026). Sem datas no
    /// corpo: o período é calculado pelo servidor, e é por isso que o helper não tem parâmetro de data.
    /// </summary>
    /// <exception cref="InvalidOperationException">Quando o servidor não respondeu 202 — com corpo.</exception>
    public static async Task<ImportRunInfo> StartUpdateBaseAsync(
        this ApiTestFixture fixture,
        string token,
        bool dryRun = false)
    {
        var response = await fixture.StartUpdateBaseRawAsync(token, dryRun);

        if (response.StatusCode != 202)
        {
            throw new InvalidOperationException(
                $"POST {UPDATE_BASE_PATH} respondeu {response.StatusCode} (esperado 202): {response.Body}");
        }

        return response.As<ImportRunInfo>();
    }

    /// <summary>
    /// <c>POST /runs/update-base</c> com a resposta bruta: as recusas (sem credenciais, sem pedido
    /// importado, intervalo acima do limite, execução ativa) precisam do corpo do <c>400</c>/<c>409</c>.
    /// </summary>
    public static async Task<HttpCallResult> StartUpdateBaseRawAsync(
        this ApiTestFixture fixture,
        string token,
        bool dryRun = false)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        return await fixture.PostAsync(UPDATE_BASE_PATH, token, new { dryRun });
    }

    // ---------------------------------------------------------------------------
    // credenciais do +Vendas na tela de configuração (feature 008, T012/T020/T026)
    // ---------------------------------------------------------------------------

    /// <summary>Rota das credenciais, usada por PUT e DELETE.</summary>
    public const string CREDENTIALS_PATH = "/imports/mais-vendas/credentials";

    /// <summary>
    /// <c>PUT /imports/mais-vendas/credentials</c> devolvendo a resposta bruta — os testes de recusa
    /// precisam do corpo do <c>400</c> e da prova de que a senha não está em lugar nenhum.
    /// <para>
    /// <b>Nulo omito, string vazia envio</b>: a distinção é justamente a que o contrato faz — "senha
    /// ausente" (manutenção do cadastro) contra "senha vazia" (recusa). Enviar <c>null</c> no JSON
    /// seria um terceiro caso que a API não especifica.
    /// </para>
    /// </summary>
    public static async Task<HttpCallResult> SaveCredentialsRawAsync(
        this ApiTestFixture fixture,
        string token,
        string? login,
        string? password)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        var payload = new Dictionary<string, object?>();

        if (login is not null)
        {
            payload["login"] = login;
        }

        if (password is not null)
        {
            payload["password"] = password;
        }

        return await fixture.PutAsync(CREDENTIALS_PATH, token, payload);
    }

    /// <summary>
    /// <c>PUT /imports/mais-vendas/credentials</c> exigindo <c>200</c>: é o setup das histórias que só
    /// precisam de "credenciais cadastradas" (US2 em diante).
    /// </summary>
    /// <exception cref="InvalidOperationException">Quando o servidor não respondeu 200 — com corpo.</exception>
    public static async Task<HttpCallResult> SaveCredentialsAsync(
        this ApiTestFixture fixture,
        string token,
        string? login,
        string? password)
    {
        var response = await fixture.SaveCredentialsRawAsync(token, login, password);

        if (response.StatusCode != 200)
        {
            throw new InvalidOperationException(
                $"PUT {CREDENTIALS_PATH} respondeu {response.StatusCode} (esperado 200): {response.Body}");
        }

        return response;
    }

    /// <summary>
    /// <c>DELETE /imports/mais-vendas/credentials</c> (resposta bruta; é <c>200</c> idempotente).
    /// </summary>
    public static async Task<HttpCallResult> DeleteCredentialsAsync(this ApiTestFixture fixture, string token)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        return await fixture.DeleteAsync(CREDENTIALS_PATH, token);
    }

    /// <summary>
    /// <c>GET /imports/mais-vendas/credentials</c> já desserializado (T020/T026): é a leitura que a
    /// vendedora faz para conferir o próprio cadastro, e a única superfície onde o <c>login</c> volta.
    /// </summary>
    /// <exception cref="InvalidOperationException">Quando o servidor não respondeu 200 — com corpo.</exception>
    public static async Task<MaisVendasCredentialsInfo> GetCredentialsAsync(this ApiTestFixture fixture, string token)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        var response = await fixture.GetAsync(CREDENTIALS_PATH, token);

        if (response.StatusCode != 200)
        {
            throw new InvalidOperationException(
                $"GET {CREDENTIALS_PATH} respondeu {response.StatusCode} (esperado 200): {response.Body}");
        }

        return response.As<MaisVendasCredentialsInfo>();
    }

    /// <summary>
    /// Cadastra as credenciais do cenário Fake, se o teste passou um par login/senha (setup compartilhado
    /// dos inícios). Os dois têm de vir preenchidos: com um só, o <c>PUT</c> recusaria (FR-002) e o erro
    /// do cadastro ofuscaria o erro do início que o teste queria medir — nesse caso preferimos não chamar
    /// nada e deixar o <c>POST /runs</c> responder pelo que é (sem credenciais cadastradas).
    /// </summary>
    private static async Task SaveScenarioCredentialsAsync(
        this ApiTestFixture fixture,
        string token,
        string? login,
        string? password)
    {
        if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        await fixture.SaveCredentialsAsync(token, login, password);
    }

    /// <summary>Corpo do início: período e modo, nada além disso (contrato da 008).</summary>
    private static object PeriodPayload(DateOnly start, DateOnly end, bool dryRun) => new
    {
        startDate = start.ToString("yyyy-MM-dd"),
        endDate = end.ToString("yyyy-MM-dd"),
        dryRun
    };

    /// <summary>
    /// Vendedora nova por teste (T035). Cada importação da suíte precisa de uma base limpa — a
    /// idempotência faria um teste herdar os pedidos do anterior e nenhuma contagem seria conferível —
    /// então o caminho é o real: <c>POST /auth/register</c> + <c>POST /auth/login</c>, com e-mail
    /// aleatório (research §7, sem atalho sem senha).
    /// </summary>
    public static async Task<(string Token, long UserId)> NewSellerAsync(
        this ApiTestFixture fixture,
        string? name = null)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        var email = $"vendedora+{Guid.NewGuid():N}@exemplo.com";

        var login = await fixture.CreateUserAsync(email, name ?? "Vendedora de Teste");

        return (login.Token, login.User.Id);
    }

    /// <summary>Contagem de pedidos da vendedora (<c>GET /orders</c>), para comparar antes/depois.</summary>
    public static async Task<int> CountOrdersAsync(this ApiTestFixture fixture, string token)
    {
        var response = await fixture.GetAsync("/orders?page=1&pageSize=1", token);

        response.StatusCode.Should().Be(200);

        return response.As<Ambrosio.DTO.Common.PagedResult<Ambrosio.DTO.Orders.OrderInfo>>().TotalCount;
    }

    /// <summary>Contagem de clientes da vendedora, para a prova de que simulação não grava (SC-011).</summary>
    public static async Task<int> CountCustomersAsync(this ApiTestFixture fixture, string token)
    {
        var response = await fixture.GetAsync("/customers?page=1&pageSize=1", token);

        response.StatusCode.Should().Be(200);

        return response.As<Ambrosio.DTO.Common.PagedResult<Ambrosio.DTO.Customers.CustomerInfo>>().TotalCount;
    }

    private static int ComputeFirstCheckDigit(int[] digits)
    {
        var sum = 0;
        var weight = 10;

        for (var i = 0; i < 9; i++)
        {
            sum += digits[i] * weight--;
        }

        return ToDigit(sum);
    }

    private static int ComputeSecondCheckDigit(int[] digits)
    {
        var sum = 0;
        var weight = 11;

        for (var i = 0; i < 10; i++)
        {
            sum += digits[i] * weight--;
        }

        return ToDigit(sum);
    }

    /// <summary>Módulo 11: resto 0 ou 1 vira dígito 0 (algoritmo clássico do CPF).</summary>
    private static int ToDigit(int sum)
    {
        var remainder = sum % 11;

        return remainder < 2 ? 0 : 11 - remainder;
    }
}
