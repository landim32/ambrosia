using Ambrosia.DTO.Enums;
using Ambrosia.DTO.Imports.Collector;

namespace Ambrosia.Infra.AppServices.MaisVendas;

/// <summary>
/// O conjunto fixo de dados que o <see cref="FakeMaisVendasCollectorAppService"/> devolve
/// (feature 006, research §3). É uma "origem de teste controlada": a homologação não pode manipular o
/// +Vendas real, e a constituição §VIII só permite testar o backend por testes de API — então os
/// critérios de aceitação viram dados determinísticos aqui.
/// <para>
/// <b>O cenário <c>ok</c> em v1</b>: 49 registros entre 26/09/2026 e 25/10/2026 com total líquido de
/// <c>R$ 123.300,58</c> (SC-001 em homologação), canais Cota e Omni, tipos venda/devolução/
/// cancelamento, uma venda cancelada gerando dois registros, e o número <c>41900001</c> em 28/09 e
/// 05/10 — os dois pedidos distintos que a US1 cenário 9 exige. Repetir o período com a mesma versão é
/// 100% <c>unchanged</c> (SC-002) porque cada linha é estável.
/// </para>
/// <para>
/// Os contadores e totais por aba (<see cref="MaisVendasFake.DeriveCounters"/>) são <b>derivados</b>
/// dos pedidos, nunca digitados à mão: é o que garante que a validação do bloco passe no cenário bom e
/// que os cenários <c>contador-divergente</c>/<c>total-divergente</c> sejam a única fonte de
/// divergência (SC-009).
/// </para>
/// </summary>
internal static class MaisVendasFake
{
    /// <summary>Primeira data do período de referência do cenário bom.</summary>
    internal static readonly DateOnly PERIOD_START = new(2026, 9, 26);

    /// <summary>Última data do período de referência do cenário bom.</summary>
    internal static readonly DateOnly PERIOD_END = new(2026, 10, 25);

    /// <summary>Total líquido esperado pelo quickstart §4 item 2 e pela SC-001.</summary>
    internal const decimal NET_TOTAL = 123300.58m;

    /// <summary>Quantidade de registros do cenário bom.</summary>
    internal const int ORDER_COUNT = 49;

    /// <summary>Os 12 CPFs matematicamente válidos usados pelos clientes (dígitos, sem pontuação).</summary>
    internal static readonly string[] CpfDigits =
    [
        "11144477735", "12345678909", "23456789173", "34567891228",
        "45678912364", "56789123482", "67891234582", "78912345664",
        "89123456728", "90123456770", "13579246828", "24681357928"
    ];

    /// <summary>Nomes dos mesmos 12 clientes, na mesma ordem (a v2 troca o nome do índice 0).</summary>
    internal static readonly string[] CustomerNames =
    [
        "Ana Beatriz Souza", "Carla Menezes", "Daniele Rocha", "Eduardo Prado",
        "Fernanda Lima", "Gustavo Nunes", "Helena Martins", "Igor Barbosa",
        "Juliana Castro", "Kleber Ramos", "Larissa Teixeira", "Marcos Vinicius"
    ];

    /// <summary>Quatro SKUs fixos: produto, tamanho e preço unitário. Um SKU por URL de foto.</summary>
    internal static readonly (string Product, string Size, decimal Price)[] Catalog =
    [
        ("Calça Inez", "38", 399.90m),
        ("Blusa Lia", "P", 189.90m),
        ("Vestido Coral", "M", 549.00m),
        ("Casaco Bremen", "G", 890.00m)
    ];

    /// <summary>URLs das fotos, uma por SKU — é o que prova a deduplicação (SC-010).</summary>
    internal static readonly string[] ImageUrls =
    [
        "https://maisvendas.somalabs.com.br/media/sku-42ca500uie.jpg",
        "https://maisvendas.somalabs.com.br/media/sku-41bl200aaa.jpg",
        "https://maisvendas.somalabs.com.br/media/sku-60vs300cor.jpg",
        "https://maisvendas.somalabs.com.br/media/sku-70cs400bre.png"
    ];

    /// <summary>Os quatro códigos de SKU correspondentes.</summary>
    internal static readonly string[] SkuCodes =
    [
        "42CA500UIE_12182", "41BL200AAA_00001", "60VS300COR_00450", "70CS400BRE_00012"
    ];

    /// <summary>
    /// Linha do cenário bom. Uma venda cancelada aparece duas vezes — a venda positiva e o
    /// cancelamento negativo com a mesma chave número/data e <see cref="Kind"/> diferente (FR-029).
    /// </summary>
    internal readonly record struct Seed(
        string Number,
        string Date,
        OrderKind Kind,
        OrderOrigin Origin,
        string Status,
        decimal Total,
        int Customer,
        int[] Skus,
        string? OriginalSaleOn = null);

    /// <summary>
    /// Os 49 registros do v1. Gerados e conferidos fora do repositório (49 linhas, líquido
    /// <c>123300.58</c>, zero colisão de chave natural); a ordem é a da lista da origem.
    /// </summary>
    internal static readonly Seed[] OkV1 =
    [
        // ---- bloco 1: 26/09..02/10 (8)
        new("41900003", "2026-09-26", OrderKind.Sale, OrderOrigin.Cota, "Faturado", 1000.00m, 1, [0]),
        new("41900002", "2026-09-27", OrderKind.Sale, OrderOrigin.Omni, "Faturado", 1100.00m, 2, [1, 2]),
        new("41900001", "2026-09-28", OrderKind.Sale, OrderOrigin.Cota, "Faturado", 1200.00m, 0, [0]),
        new("41900004", "2026-09-29", OrderKind.Sale, OrderOrigin.Omni, "Faturado", 1300.00m, 0, [3]),
        new("41900005", "2026-09-30", OrderKind.Sale, OrderOrigin.Cota, "Faturado", 1400.00m, 3, [2]),
        new("41900006", "2026-09-30", OrderKind.Return, OrderOrigin.Cota, "Devolvido", -589.80m, 4, [0], "2026-09-12"),
        new("41900007", "2026-10-01", OrderKind.Sale, OrderOrigin.Omni, "Faturado", 1500.00m, 5, [1]),
        new("41900008", "2026-10-02", OrderKind.Sale, OrderOrigin.Cota, "Separado", 1600.00m, 6, [0, 3]),

        // ---- bloco 2: 03/10..09/10 (8)
        new("41900009", "2026-10-03", OrderKind.Sale, OrderOrigin.Omni, "Faturado", 1700.00m, 7, [2]),
        new("41900010", "2026-10-04", OrderKind.Sale, OrderOrigin.Cota, "Faturado", 1800.00m, 8, [3]),
        new("41900001", "2026-10-05", OrderKind.Sale, OrderOrigin.Cota, "Faturado", 1900.00m, 0, [0]),
        new("41900012", "2026-10-06", OrderKind.Sale, OrderOrigin.Omni, "Faturado", 2000.00m, 9, [1, 2]),
        new("41900013", "2026-10-07", OrderKind.Sale, OrderOrigin.Cota, "Faturado", 2100.00m, 10, [0]),
        new("41900014", "2026-10-07", OrderKind.Return, OrderOrigin.Omni, "Devolvido", -199.90m, 11, [2], "2026-09-20"),
        new("41900015", "2026-10-08", OrderKind.Sale, OrderOrigin.Cota, "Entregue", 2200.00m, 1, [0, 1]),
        new("41900016", "2026-10-09", OrderKind.Sale, OrderOrigin.Omni, "Faturado", 2300.00m, 2, [3]),

        // ---- bloco 3: 10/10..16/10 (15) — o 15º registro é o que falha em "falha-bloco3"
        new("41900017", "2026-10-10", OrderKind.Sale, OrderOrigin.Cota, "Faturado", 2400.00m, 3, [2]),
        new("41900018", "2026-10-11", OrderKind.Sale, OrderOrigin.Omni, "Faturado", 2500.00m, 4, [3]),
        new("41900019", "2026-10-12", OrderKind.Sale, OrderOrigin.Cota, "Faturado", 2600.00m, 5, [0]),
        new("41900020", "2026-10-13", OrderKind.Sale, OrderOrigin.Omni, "Faturado", 2700.00m, 6, [1]),
        new("41900021", "2026-10-13", OrderKind.Sale, OrderOrigin.Cota, "Faturado", 2800.00m, 7, [2]),
        new("41900022", "2026-10-14", OrderKind.Sale, OrderOrigin.Omni, "Faturado", 2900.00m, 8, [0, 3]),
        new("41900023", "2026-10-14", OrderKind.Sale, OrderOrigin.Cota, "Separado", 3000.00m, 9, [2]),
        new("41900024", "2026-10-14", OrderKind.Sale, OrderOrigin.Omni, "Faturado", 3100.00m, 10, [1]),
        new("41900025", "2026-10-15", OrderKind.Sale, OrderOrigin.Cota, "Faturado", 3200.00m, 11, [0]),

        // venda cancelada na origem: dois registros com a mesma chave número/data e tipos diferentes
        new("41900026", "2026-10-15", OrderKind.Sale, OrderOrigin.Omni, "Faturado", 2450.00m, 1, [3]),
        new("41900026", "2026-10-15", OrderKind.Cancellation, OrderOrigin.Omni, "Cancelado", -2450.00m, 1, [3], "2026-10-15"),

        new("41900027", "2026-10-16", OrderKind.Sale, OrderOrigin.Cota, "Faturado", 3300.00m, 2, [2]),
        new("41900028", "2026-10-16", OrderKind.Sale, OrderOrigin.Omni, "Faturado", 3400.00m, 3, [1, 3]),
        new("41900029", "2026-10-16", OrderKind.Sale, OrderOrigin.Cota, "Faturado", 3500.00m, 4, [0]),
        new("41900030", "2026-10-16", OrderKind.Sale, OrderOrigin.Omni, "Faturado", 3600.00m, 5, [3]),

        // ---- bloco 4: 17/10..23/10 (12)
        new("41900031", "2026-10-17", OrderKind.Sale, OrderOrigin.Omni, "Faturado", 3700.00m, 6, [2]),
        new("41900032", "2026-10-17", OrderKind.Sale, OrderOrigin.Cota, "Faturado", 3800.00m, 7, [0]),
        new("41900033", "2026-10-18", OrderKind.Sale, OrderOrigin.Omni, "Entregue", 3900.00m, 8, [3]),
        new("41900034", "2026-10-18", OrderKind.Sale, OrderOrigin.Cota, "Faturado", 4000.00m, 9, [1]),
        new("41900035", "2026-10-19", OrderKind.Sale, OrderOrigin.Omni, "Faturado", 4100.00m, 10, [0, 1]),
        new("41900036", "2026-10-19", OrderKind.Sale, OrderOrigin.Cota, "Faturado", 4200.00m, 11, [2]),

        new("41900037", "2026-10-20", OrderKind.Sale, OrderOrigin.Cota, "Faturado", 3120.50m, 1, [3]),
        new("41900037", "2026-10-20", OrderKind.Cancellation, OrderOrigin.Cota, "Cancelado", -3120.50m, 1, [3], "2026-10-20"),

        new("41900038", "2026-10-21", OrderKind.Sale, OrderOrigin.Omni, "Separado", 4300.00m, 2, [0]),
        new("41900039", "2026-10-22", OrderKind.Sale, OrderOrigin.Cota, "Faturado", 4400.00m, 3, [2]),
        new("41900040", "2026-10-22", OrderKind.Sale, OrderOrigin.Omni, "Entregue", 4500.00m, 4, [1]),
        new("41900041", "2026-10-23", OrderKind.Sale, OrderOrigin.Cota, "Faturado", 4600.00m, 5, [3]),

        // ---- bloco 5: 24/10..25/10 (6)
        new("41900042", "2026-10-24", OrderKind.Sale, OrderOrigin.Omni, "Faturado", 4700.00m, 6, [2]),
        new("41900043", "2026-10-24", OrderKind.Sale, OrderOrigin.Cota, "Faturado", 4800.00m, 7, [0, 3]),
        new("41900044", "2026-10-24", OrderKind.Sale, OrderOrigin.Omni, "Separado", 4900.00m, 8, [1]),

        new("41900045", "2026-10-25", OrderKind.Sale, OrderOrigin.Cota, "Faturado", 1899.90m, 9, [0]),
        new("41900045", "2026-10-25", OrderKind.Cancellation, OrderOrigin.Cota, "Cancelado", -1899.90m, 9, [0], "2026-10-25"),

        new("41900046", "2026-10-25", OrderKind.Sale, OrderOrigin.Omni, "Faturado", 6090.28m, 10, [2])
    ];

    /// <summary>
    /// Número do 15º registro do 3º bloco (T072): é o pedido que <c>falha-bloco3</c> faz estourar, com
    /// <c>order_number</c> acima dos 60 caracteres da coluna, depois de 14 pedidos já gravados.
    /// </summary>
    internal const string FAILING_ORDER_NUMBER = "41900030";

    /// <summary>
    /// O v2 (T072, senha <c>v2</c>): a origem alterou valor e status de alguns pedidos e o nome de um
    /// cliente. É a prova de "atualizado" com <c>changedFields</c> (US3-5) e de "nome atualizado"
    /// preservando o resto do cliente (US5-3). As demais linhas são idênticas ao v1, para o relatório
    /// mostrar uma mistura de <c>updated</c> e <c>unchanged</c>.
    /// </summary>
    internal static Seed[] OkV2()
    {
        // Só linhas cujo sinal é compatível com a mudança: quatro vendas (duas de cada canal) e o
        // status de uma devolução. Os índices são posições em OkV1, e nenhum cancelamento é tocado —
        // mudar o sinal de uma devolução/cancelamento quebraria a FR-013 da feature 001 e o bloco
        // seria recusado pela validação de totais, não por decisão de teste.
        var tweaks = new Dictionary<int, (decimal? Total, string? Status)>
        {
            [3] = (1350.00m, "Entregue"),      // 41900004, 29/09, Omni  (venda)
            [7] = (null, "Faturado"),          // 41900008, 02/10, Cota  (venda "Separado" → "Faturado")
            [17] = (2550.00m, null),           // 41900018, 11/10, Omni  (venda)
            [21] = (2950.00m, "Entregue"),     // 41900022, 14/10, Omni  (venda: dois campos)
            [13] = (null, "Devolução aprovada"), // 41900014, 07/10      (devolução: só status)
            [41] = (4550.00m, null)            // 41900040, 22/10, Omni  (venda)
        };

        var rows = new List<Seed>(OkV1.Length);

        for (var i = 0; i < OkV1.Length; i++)
        {
            var seed = OkV1[i];

            if (tweaks.TryGetValue(i, out var tweak))
            {
                seed = seed with
                {
                    Total = tweak.Total ?? seed.Total,
                    Status = tweak.Status ?? seed.Status
                };
            }

            rows.Add(seed);
        }

        return [.. rows];
    }

    /// <summary>
    /// O v3 (T072, senha <c>v3</c>): omite um pedido do 2º bloco, para provar que o pedido importado
    /// antes e ausente da origem é reportado como <c>missing</c> sem ser apagado (FR-027, US3-7).
    /// </summary>
    internal static Seed[] OkV3() =>
        [.. OkV1.Where((_, index) => index != 12)];   // derruba "41900013" de 07/10 (bloco 2)

    /// <summary>
    /// Converte uma linha em <see cref="CollectedOrder"/> com itens, cliente e foto por SKU.
    /// O CPF é passado de fora porque o cenário <c>cpf-invalido</c> precisa entregue um documento
    /// ausente e um com dígito verificador errado (T084) — a validação em si é do Domain.
    /// </summary>
    internal static CollectedOrder ToCollected(Seed seed, int version, string? cpfDigits)
    {
        var customer = seed.Customer % CustomerNames.Length;

        // v2 troca o nome de um único cliente (US5-3): é o índice 0, o mesmo que tem três pedidos.
        var name = version >= 2 && customer == 0 ? "Ana Beatriz S. Mello" : CustomerNames[customer];

        var items = new List<CollectedOrderItem>(seed.Skus.Length);

        for (var i = 0; i < seed.Skus.Length; i++)
        {
            var sku = seed.Skus[i] % Catalog.Length;
            var (product, size, price) = Catalog[sku];

            items.Add(new CollectedOrderItem
            {
                LineNo = i + 1,
                ProductName = product,
                SkuRef = SkuCodes[sku],
                Size = size,
                // O sinal do item segue o do pedido: devolução e cancelamento entram negativos.
                Price = seed.Total < 0m ? -Math.Abs(price) : price,
                ImageUrl = ImageUrls[sku]
            });
        }

        var date = ParseDate(seed.Date);

        return new CollectedOrder
        {
            OrderNumber = seed.Number,
            OrderDate = date,
            Kind = seed.Kind,
            Channel = ChannelOf(seed.Origin),
            Origin = seed.Origin,
            Status = seed.Status,
            Total = seed.Total,
            OriginalSaleOn = seed.OriginalSaleOn is null ? null : ParseDate(seed.OriginalSaleOn),
            CancelledOn = seed.Kind == OrderKind.Cancellation ? date : null,
            ExternalId = ExternalIdOf(seed),
            CustomerName = name,
            CpfDigits = cpfDigits,
            Items = items
        };
    }

    /// <summary>Canal exibido pela aba da origem: cota é "Linx", omni é "Online".</summary>
    internal static string ChannelOf(OrderOrigin origin) =>
        origin == OrderOrigin.Cota ? "Linx" : "Online";

    /// <summary>
    /// Contadores e totais por aba, <b>derivados</b> dos pedidos coletados (research §10): o fake
    /// reproduz o que a origem mostraria se a lista estivesse correta. Os cenários de divergência
    /// perturbam o resultado desta função — nunca a lista de pedidos.
    /// </summary>
    internal static (Dictionary<CollectorTabKey, int> Counters, Dictionary<CollectorTabKey, decimal> Totals)
        DeriveCounters(IReadOnlyList<CollectedOrder> orders)
    {
        var counters = new Dictionary<CollectorTabKey, int>();
        var totals = new Dictionary<CollectorTabKey, decimal>();

        foreach (var origin in new[] { OrderOrigin.Cota, OrderOrigin.Omni })
        {
            var inOrigin = orders.Where(o => o.Origin == origin).ToList();

            // "Todos" é a soma da aba inteira; as demais contam por tipo, com o total líquido exibido.
            Add(counters, totals, origin, CollectorTab.All, inOrigin.Count, Sum(inOrigin));
            Add(counters, totals, origin, CollectorTab.Sale, Count(inOrigin, OrderKind.Sale), SumOf(inOrigin, OrderKind.Sale));
            Add(counters, totals, origin, CollectorTab.Return, Count(inOrigin, OrderKind.Return), SumOf(inOrigin, OrderKind.Return));
            Add(counters, totals, origin, CollectorTab.Cancellation, Count(inOrigin, OrderKind.Cancellation), SumOf(inOrigin, OrderKind.Cancellation));
        }

        return (counters, totals);
    }

    private static void Add(
        Dictionary<CollectorTabKey, int> counters,
        Dictionary<CollectorTabKey, decimal> totals,
        OrderOrigin origin,
        CollectorTab tab,
        int count,
        decimal total)
    {
        var key = new CollectorTabKey(origin, tab);

        counters[key] = count;
        totals[key] = total;
    }

    private static int Count(IEnumerable<CollectedOrder> orders, OrderKind kind) =>
        orders.Count(o => o.Kind == kind);

    private static decimal Sum(IEnumerable<CollectedOrder> orders) =>
        Math.Round(orders.Sum(o => o.Total), 2, MidpointRounding.AwayFromZero);

    private static decimal SumOf(IEnumerable<CollectedOrder> orders, OrderKind kind) =>
        Math.Round(orders.Where(o => o.Kind == kind).Sum(o => o.Total), 2, MidpointRounding.AwayFromZero);

    /// <summary>Data ISO do seed (a origem real usa "24 Out"; o cenário <c>sem-ano</c> exibe esse formato).</summary>
    internal static DateOnly ParseDate(string iso)
    {
        var parts = iso.Split('-');

        return new DateOnly(int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]));
    }

    /// <summary>Identificador externo estável: muda só com a chave natural, para não quebrar o hash.</summary>
    private static string ExternalIdOf(Seed seed) =>
        $"{seed.Number}-{seed.Date}-{seed.Kind}".ToLowerInvariant();

    /// <summary>
    /// Nome do recurso embutido de uma foto, por SKU (T088). Um binário diferente por SKU: com o mesmo
    /// arquivo em dois SKUs a deduplicação por SHA-256 do storage juntaria as duas fotos, e a contagem
    /// "4 SKUs → 4 fotos" do relatório deixaria de ser verificável.
    /// </summary>
    internal static string ImageResourceName(int skuIndex) =>
        skuIndex switch
        {
            1 => "other.jpg",
            2 => "small.webp",
            3 => "small.png",
            _ => "small.jpg"
        };

    /// <summary>MIME type correspondente ao recurso embutido.</summary>
    internal static string ImageMimeType(int skuIndex) =>
        skuIndex switch
        {
            2 => "image/webp",
            3 => "image/png",
            _ => "image/jpeg"
        };
}
