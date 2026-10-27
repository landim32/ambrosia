using System.Text.Json;
using Ambrosia.Domain.Common;
using Ambrosia.Domain.Exceptions;
using Ambrosia.DTO.Enums;
using Ambrosia.DTO.Imports.Collector;
using Ambrosia.DTO.Settings;
using Ambrosia.Infra.Interfaces.AppServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace Ambrosia.Infra.AppServices.MaisVendas;

/// <summary>
/// Coletor real do +Vendas (feature 006, research §2/§9/§10/§12). Chromium headless dentro da API,
/// para que a senha da vendedora nunca saia do processo que a recebeu (research §1) — um serviço Node
/// separado a repassaria por rede interna.
/// <para>
/// <b>Somente leitura por construção</b> (FR-042, SC-012): toda requisição passa pelo
/// <see cref="NavigationGuard"/>, os únicos cliques são "Próximo" e "Entrar" do login, e as chamadas à
/// API interna são consultas. "Novo carrinho", "Sair" e o menu BOX nunca aparecem em seletor nenhum.
/// O ritmo é do <see cref="OriginRateLimiter"/>.
/// </para>
/// <para>
/// <b>Sessão nova por execução</b> (FR-044): <c>BrowserContext</c> criado sem <c>storageState</c>, com
/// <c>AcceptDownloads = false</c> e <c>IgnoreHTTPSErrors = false</c>, fechado no
/// <see cref="IMaisVendasCollectorSession.DisposeAsync"/>, em sucesso, falha ou cancelamento.
/// </para>
/// <para>
/// <b>Fonte dos dados</b> (research §10): o navegador faz só o login; os pedidos vêm da mesma API
/// JSON que a tela do +Vendas usa, com o token da própria sessão. Como <see cref="CollectedBlock"/>
/// não depende da fonte, ajustar a leitura não alcança o Domain.
/// </para>
/// <para>
/// Não coberto por teste automatizado (constituição §VIII): o backend é testado com o coletor Fake da
/// homologação, e este arquivo é validado manualmente com uma conta real, sempre em Simulação primeiro.
/// </para>
/// </summary>
public class PlaywrightMaisVendasCollectorAppService : IMaisVendasCollectorAppService, IAsyncDisposable
{
    /// <summary>Versão registrada em <c>import_runs.collector_version</c> (data-model §2).</summary>
    public const string VERSION = "playwright-1.0";

    /// <summary>Timeout de navegação/ação; estouro aqui é caso de retentativa (FR-045).</summary>
    private const int NAVIGATION_TIMEOUT_MS = 30_000;

    private readonly MaisVendasSettings _settings;
    private readonly OriginRateLimiter _limiter;
    private readonly ILogger<PlaywrightMaisVendasCollectorAppService> _logger;

    /// <summary>
    /// Browser único do processo, criado sob demanda: um <c>IBrowser</c> por processo é a recomendação
    /// do Playwright (lançar navegador é caro), e o isolamento por execução vem do
    /// <c>BrowserContext</c> novo, que é barato e derruba cookies junto com ele.
    /// </summary>
    private readonly SemaphoreSlim _browserGate = new(1, 1);

    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public PlaywrightMaisVendasCollectorAppService(
        IOptions<MaisVendasSettings> settings,
        OriginRateLimiter limiter,
        ILogger<PlaywrightMaisVendasCollectorAppService> logger)
    {
        _settings = settings.Value;
        _limiter = limiter;
        _logger = logger;
    }

    /// <inheritdoc />
    public string CollectorVersion => VERSION;

    /// <inheritdoc />
    public async Task<IMaisVendasCollectorSession> OpenSessionAsync(
        MaisVendasCredentials credentials,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        var browser = await EnsureBrowserAsync(cancellationToken);
        var guard = new NavigationGuard(_settings.BaseUrl);
        var logger = _logger;

        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            AcceptDownloads = false,
            IgnoreHTTPSErrors = false,
            ViewportSize = new ViewportSize { Width = 1366, Height = 900 },
            Locale = "pt-BR",
            TimezoneId = SaoPauloCalendar.TIME_ZONE_ID
        });

        // Allowlist no nível de rede: favicon, fonte, analytics e XHR passam todos por aqui. Fora da
        // lista é abortado em silêncio (research §9).
        await context.RouteAsync("**/*", route =>
        {
            var request = route.Request;

            // Navegação só para as rotas da coleta; recursos da página (JS, CSS, XHR) só dos hosts
            // do +Vendas.
            var allowed = request.IsNavigationRequest
                ? guard.IsAllowed(request.Url)
                : guard.IsAllowedResource(request.Url);

            if (allowed)
            {
                return route.ContinueAsync();
            }

            logger.LogDebug("Coleta: requisição abortada para {Url}.", NavigationGuard.Describe(request.Url));

            return route.AbortAsync();
        });

        var page = await context.NewPageAsync();

        page.SetDefaultTimeout(NAVIGATION_TIMEOUT_MS);

        return new PlaywrightSession(context, page, guard, credentials, _limiter, _settings, _logger);
    }

    private async Task<IBrowser> EnsureBrowserAsync(CancellationToken cancellationToken)
    {
        await _browserGate.WaitAsync(cancellationToken);

        try
        {
            if (_browser is { IsConnected: true })
            {
                return _browser;
            }

            _playwright ??= await Microsoft.Playwright.Playwright.CreateAsync();

            // Headless, sem argumentos que desliguem a segurança do navegador, e sem ignorar TLS.
            _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true
            });

            _logger.LogInformation("Chromium iniciado para a coleta do +Vendas.");

            return _browser;
        }
        finally
        {
            _browserGate.Release();
        }
    }

    /// <summary>Fecha navegador e driver no shutdown do host.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_browser is not null)
        {
            await _browser.CloseAsync();
            _browser = null;
        }

        _playwright?.Dispose();
        _playwright = null;
        _browserGate.Dispose();
    }

    /// <summary>
    /// Sessão de coleta: um contexto, uma página e o estado da leitura. É o único lugar do sistema que
    /// conhece o +Vendas por dentro — telas de login, rotas e o formato da API interna — e nada disso
    /// vaza para o Domain.
    /// <para>
    /// O navegador só faz o login (duas etapas: usuário → "Próximo" → senha → "Entrar"). Depois, a
    /// coleta lê a mesma API JSON que a tela usa, com o token que a própria SPA envia: a lista
    /// <c>salesperson/orders</c> do período, <c>client-data</c> para nome e CPF, <c>order-detail</c> para
    /// os itens e <c>order-detail-info</c> para nome e foto dos produtos. Todas são leituras (GET, e o
    /// POST de <c>client-data</c> só consulta).
    /// </para>
    /// </summary>
    private sealed class PlaywrightSession : IMaisVendasCollectorSession
    {
        /// <summary>
        /// Textos que o coletor NUNCA clica (FR-042). Os únicos cliques são "Próximo" e "Entrar" do
        /// login; esta lista existe para a revisão de liberação conferir seletor por seletor.
        /// </summary>
        internal static readonly string[] ForbiddenClickLabels =
        [
            "Novo carrinho",
            "Sair",
            "Logout",
            "BOX",
            "Cadastre-se",
            "Esqueceu sua senha?",
            "Excluir",
            "Finalizar",
            "Pagar"
        ];

        private const string API_BASE = "https://" + NavigationGuard.API_HOST;

        /// <summary>Lista de pedidos da vendedora; a SPA a chama logo depois do login.</summary>
        private const string ORDERS_PATH = "/salesperson/orders";

        private const string CLIENT_DATA_PATH = "/salesperson/orders/client-data";
        private const string ORDER_DETAIL_PATH = "/salesperson/order-detail";
        private const string PRODUCT_INFO_PATH = "/salesperson/order-detail-info";

        /// <summary>Marcadores da origem: o cancelamento é um registro próprio, e a devolução é um canal.</summary>
        private const string CANCELLED_EVENT = "CANCELADO";
        private const string RETURN_CHANNEL = "DEVOLUCAO";

        /// <summary>Tempo de espera pelo token e pelo código da vendedora depois do login.</summary>
        private const int SESSION_READY_TIMEOUT_MS = 30_000;

        /// <summary>
        /// A foto de <c>order-detail-info</c> é a miniatura 55×55 do CDN da VTEX (<c>/ids/{id}-55-55/</c>).
        /// O mesmo arquivo é servido em outro tamanho trocando esse segmento.
        /// </summary>
        private const string IMAGE_SIZE_SEGMENT = "-600-600";

        private static readonly System.Text.RegularExpressions.Regex VtexSizeSegment =
            new(@"(/arquivos/ids/\d+)-\d+-\d+/", System.Text.RegularExpressions.RegexOptions.Compiled);

        private static readonly System.Globalization.CultureInfo PtBr = new("pt-BR");

        private readonly IBrowserContext _context;
        private readonly IPage _page;
        private readonly NavigationGuard _guard;
        private readonly MaisVendasCredentials _credentials;
        private readonly OriginRateLimiter _limiter;
        private readonly MaisVendasSettings _settings;
        private readonly ILogger _logger;

        /// <summary>Nome e foto por referência de produto: o mesmo SKU em vários pedidos é lido uma vez.</summary>
        private readonly Dictionary<string, ProductInfo> _products = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Último <c>Authorization</c> que a SPA enviou à API. O token muda logo depois do login, então
        /// sempre vale o mais recente — e um 401 é tentado de novo com ele.
        /// </summary>
        private string? _authorization;

        /// <summary>Código da vendedora e marca, lidos da primeira chamada da SPA à lista de pedidos.</summary>
        private string? _salesCode;

        private string? _brands;

        private bool _disposed;

        public PlaywrightSession(
            IBrowserContext context,
            IPage page,
            NavigationGuard guard,
            MaisVendasCredentials credentials,
            OriginRateLimiter limiter,
            MaisVendasSettings settings,
            ILogger logger)
        {
            _context = context;
            _page = page;
            _guard = guard;
            _credentials = credentials;
            _limiter = limiter;
            _settings = settings;
            _logger = logger;

            _context.Request += (_, request) => CaptureSessionData(request);
        }

        /// <inheritdoc />
        public async Task<MaisVendasLoginResult> LoginAsync(CancellationToken cancellationToken = default)
        {
            EnsureOpen();

            await _limiter.WaitAsync(cancellationToken);
            await NavigateAsync($"{_settings.BaseUrl.TrimEnd('/')}/login", cancellationToken);

            // 1ª etapa: usuário (e-mail ou nome de usuário) e "Próximo". O id "submit" se repete no
            // "Cadastre-se", por isso o botão é escolhido pelo texto.
            await _page.Locator("#username").FillAsync(_credentials.Login);

            if (await HasUnsupportedStepAsync())
            {
                return MaisVendasLoginResult.Unsupported();
            }

            await _limiter.WaitAsync(cancellationToken);
            await _page.Locator("button:has-text('Próximo')").First.ClickAsync();

            // 2ª etapa: a senha só aparece quando o usuário existe. Sem ela, a origem recusou o login.
            var password = _page.Locator("input[type='password']").First;

            try
            {
                await password.WaitForAsync(new() { Timeout = NAVIGATION_TIMEOUT_MS });
            }
            catch (TimeoutException)
            {
                return await HasUnsupportedStepAsync()
                    ? MaisVendasLoginResult.Unsupported()
                    : MaisVendasLoginResult.Refused();
            }

            await password.FillAsync(_credentials.Password);

            await _limiter.WaitAsync(cancellationToken);
            await _page.Locator("button:has-text('Entrar')").First.ClickAsync();

            // O desafio também pode aparecer depois do submit (2FA intersticial).
            if (await HasUnsupportedStepAsync())
            {
                return MaisVendasLoginResult.Unsupported();
            }

            if (!await WaitForPostLoginAsync(cancellationToken))
            {
                return MaisVendasLoginResult.Refused();
            }

            await WaitForSessionDataAsync(cancellationToken);

            // Só o código da vendedora sai daqui, e só para virar hash com pepper (research §8).
            return MaisVendasLoginResult.Accepted(_salesCode);
        }

        /// <inheritdoc />
        public async Task<CollectedBlock> CollectBlockAsync(
            DateOnly from,
            DateOnly to,
            CancellationToken cancellationToken = default)
        {
            EnsureOpen();

            var query =
                $"startDate={from:yyyy-MM-dd}&endDate={to:yyyy-MM-dd}" +
                $"&salesCode[]={Uri.EscapeDataString(_salesCode!)}&brands={Uri.EscapeDataString(_brands!)}" +
                "&transaction=true&removeExternalProducts=false";

            using var list = await GetJsonAsync($"{ORDERS_PATH}?{query}", cancellationToken);

            var entries = list.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array
                ? data.EnumerateArray().Select(e => e.Clone()).ToList()
                : throw new MaisVendasSourceException("a lista de pedidos veio fora do formato esperado");

            var customers = await ReadCustomersAsync(entries, cancellationToken);

            var orders = new List<CollectedOrder>(entries.Count);

            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                orders.Add(await BuildOrderAsync(entry, customers, cancellationToken));
            }

            // Contadores e total da própria lista, por tipo, conferidos pelo BlockValidator contra os
            // pedidos montados: um registro perdido ou com o tipo trocado na montagem derruba o bloco.
            var counters = new Dictionary<CollectorTabKey, int>
            {
                [new(OrderOrigin.Cota, CollectorTab.All)] = entries.Count,
                [new(OrderOrigin.Cota, CollectorTab.Sale)] = entries.Count(e => KindOf(e) == OrderKind.Sale),
                [new(OrderOrigin.Cota, CollectorTab.Return)] = entries.Count(e => KindOf(e) == OrderKind.Return),
                [new(OrderOrigin.Cota, CollectorTab.Cancellation)] = entries.Count(e => KindOf(e) == OrderKind.Cancellation)
            };

            var totals = new Dictionary<CollectorTabKey, decimal>
            {
                [new(OrderOrigin.Cota, CollectorTab.All)] =
                    Math.Round(entries.Sum(e => Money(e, "value")), 2, MidpointRounding.AwayFromZero)
            };

            _logger.LogInformation(
                "Coleta de {From:yyyy-MM-dd} a {To:yyyy-MM-dd}: {Count} pedido(s) na origem.",
                from, to, entries.Count);

            return new CollectedBlock
            {
                Orders = orders,
                TabCounters = counters,
                TabTotals = totals
            };
        }

        /// <inheritdoc />
        public async Task<CollectedImage> DownloadImageAsync(
            string url,
            long maxBytes,
            CancellationToken cancellationToken = default)
        {
            EnsureOpen();

            // As fotos ficam no CDN público da marca (VTEX), não no +Vendas: nada de token nem cookie
            // da vendedora vai junto — só a URL, conferida pela allowlist de imagens.
            if (!_guard.IsAllowedImage(url))
            {
                throw new MaisVendasSourceException($"foto fora da allowlist: {NavigationGuard.Describe(url)}");
            }

            await _limiter.WaitAsync(cancellationToken);

            IAPIResponse response;

            try
            {
                response = await _context.APIRequest.GetAsync(url, new() { Timeout = NAVIGATION_TIMEOUT_MS });
            }
            catch (PlaywrightException ex)
            {
                throw new MaisVendasSourceException("o download da foto não completou", ex);
            }

            if (!response.Ok)
            {
                throw new MaisVendasSourceException($"a foto respondeu {response.Status}");
            }

            var body = await response.BodyAsync();

            if (body.Length > maxBytes)
            {
                throw new MaisVendasSourceException("a foto excede o tamanho máximo aceito pelo storage");
            }

            var mimeType = response.Headers.TryGetValue("content-type", out var declared)
                ? declared.Split(';')[0].Trim().ToLowerInvariant()
                : "application/octet-stream";

            return new CollectedImage
            {
                Content = new MemoryStream(body, writable: false),
                MimeType = mimeType,
                SizeBytes = body.Length
            };
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            // FR-044: fechar o contexto derruba cookies, storage local e a sessão remota.
            try
            {
                await _context.CloseAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "O contexto de coleta não fechou de forma limpa.");
            }
        }

        // -------------------------------------------------------------- sessão

        /// <summary>
        /// Lê das requisições da SPA o token e, da lista de pedidos, o código da vendedora e a marca.
        /// Nenhum dos três vai para log nem para o banco.
        /// </summary>
        private void CaptureSessionData(IRequest request)
        {
            if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri)
                || !string.Equals(uri.Host, NavigationGuard.API_HOST, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (request.Headers.TryGetValue("authorization", out var authorization)
                && !string.IsNullOrWhiteSpace(authorization))
            {
                _authorization = authorization;
            }

            if (_salesCode is null && uri.AbsolutePath.Equals(ORDERS_PATH, StringComparison.OrdinalIgnoreCase))
            {
                var query = System.Web.HttpUtility.ParseQueryString(uri.Query);

                var salesCode = query["salesCode[]"];
                var brands = query["brands"];

                if (!string.IsNullOrWhiteSpace(salesCode) && !string.IsNullOrWhiteSpace(brands))
                {
                    _brands = brands;
                    _salesCode = salesCode;
                }
            }
        }

        /// <summary>Espera a SPA chamar a lista de pedidos com o token, logo depois do login.</summary>
        private async Task WaitForSessionDataAsync(CancellationToken cancellationToken)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(SESSION_READY_TIMEOUT_MS);

            while (_salesCode is null || _authorization is null)
            {
                if (DateTime.UtcNow > deadline)
                {
                    throw new MaisVendasSourceException(
                        "o login entrou, mas a origem não carregou a lista de pedidos da vendedora");
                }

                await Task.Delay(250, cancellationToken);
            }
        }

        // -------------------------------------------------------------- API interna

        /// <summary>
        /// Leitura na API interna com o token da sessão. 401 tenta de novo uma vez, com o token mais
        /// recente que a SPA tiver enviado; os demais erros viram falha de origem (bloco repetível).
        /// </summary>
        private async Task<JsonDocument> GetJsonAsync(string pathAndQuery, CancellationToken cancellationToken) =>
            await SendJsonAsync(pathAndQuery, body: null, cancellationToken);

        private async Task<JsonDocument> SendJsonAsync(
            string pathAndQuery,
            object? body,
            CancellationToken cancellationToken)
        {
            var url = API_BASE + pathAndQuery;

            _guard.EnsureAllowed(url);

            for (var attempt = 1; ; attempt++)
            {
                await _limiter.WaitAsync(cancellationToken);

                var options = new APIRequestContextOptions
                {
                    Method = body is null ? "GET" : "POST",
                    Headers = new Dictionary<string, string>
                    {
                        ["authorization"] = _authorization ?? string.Empty,
                        ["accept"] = "application/json"
                    },
                    Timeout = NAVIGATION_TIMEOUT_MS
                };

                if (body is not null)
                {
                    options.DataObject = body;
                }

                IAPIResponse response;

                try
                {
                    response = await _context.APIRequest.FetchAsync(url, options);
                }
                catch (PlaywrightException ex)
                {
                    throw new MaisVendasSourceException("a API da origem não respondeu", ex);
                }

                if (response.Status == 401 && attempt == 1)
                {
                    await Task.Delay(1500, cancellationToken);
                    continue;
                }

                if (!response.Ok)
                {
                    throw new MaisVendasSourceException(
                        $"a API da origem respondeu {response.Status} em {NavigationGuard.Describe(url)}");
                }

                try
                {
                    return JsonDocument.Parse(await response.TextAsync());
                }
                catch (JsonException ex)
                {
                    throw new MaisVendasSourceException(
                        $"a API da origem devolveu um corpo inválido em {NavigationGuard.Describe(url)}", ex);
                }
            }
        }

        /// <summary>Nome e CPF dos clientes do bloco, numa consulta só (<c>client-data</c>).</summary>
        private async Task<Dictionary<string, (string? Name, string? Cpf)>> ReadCustomersAsync(
            List<JsonElement> entries,
            CancellationToken cancellationToken)
        {
            var result = new Dictionary<string, (string? Name, string? Cpf)>(StringComparer.Ordinal);

            var request = entries
                .Select(e => new { orderId = Text(e, "orderId"), clientId = Text(e, "clientId") })
                .Where(r => r.orderId is not null && r.clientId is not null)
                .DistinctBy(r => (r.orderId, r.clientId))
                .ToList();

            if (request.Count == 0)
            {
                return result;
            }

            using var response = await SendJsonAsync(CLIENT_DATA_PATH, new { orderData = request }, cancellationToken);

            if (response.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new MaisVendasSourceException("os dados de cliente vieram fora do formato esperado");
            }

            foreach (var row in response.RootElement.EnumerateArray())
            {
                var clientId = Text(row, "clientId");

                if (clientId is null || !row.TryGetProperty("clientData", out var client)
                    || client.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                result[clientId] = (Text(client, "nome"), Text(client, "cpf"));
            }

            return result;
        }

        /// <summary>Monta o pedido com itens (<c>order-detail</c>) e nome/foto dos produtos.</summary>
        private async Task<CollectedOrder> BuildOrderAsync(
            JsonElement entry,
            Dictionary<string, (string? Name, string? Cpf)> customers,
            CancellationToken cancellationToken)
        {
            var orderId = Text(entry, "orderId")
                ?? throw new MaisVendasSourceException("um pedido da origem veio sem número");

            var createdAt = Instant(entry, "creationDate")
                ?? throw new MaisVendasSourceException($"o pedido {orderId} veio sem data");

            var orderDate = DateOnly.FromDateTime(SaoPauloCalendar.ToSaoPaulo(createdAt));
            var kind = KindOf(entry);
            var total = Money(entry, "value");
            var clientId = Text(entry, "clientId");

            var detailQuery =
                $"orderId={Uri.EscapeDataString(orderId)}&salesCode={Uri.EscapeDataString(_salesCode!)}" +
                $"&orderCreationDate={createdAt:yyyy-MM-dd}&idPacote={Text(entry, "idPacote")}" +
                $"&brandId={Text(entry, "brandId")}&omni=false&orderOriginal=";

            using var detail = await GetJsonAsync($"{ORDER_DETAIL_PATH}?{detailQuery}", cancellationToken);

            var products = detail.RootElement.TryGetProperty("products", out var array) && array.ValueKind == JsonValueKind.Array
                ? array.EnumerateArray().Select(p => p.Clone()).ToList()
                : [];

            await LoadProductInfoAsync(products, clientId, cancellationToken);

            // A data do detalhe é a da venda original: na devolução e no cancelamento, a da lista é a
            // do evento.
            var originalSale = Instant(detail.RootElement, "creationDate") is { } original
                ? DateOnly.FromDateTime(SaoPauloCalendar.ToSaoPaulo(original))
                : (DateOnly?)null;

            var items = new List<CollectedOrderItem>(products.Count);
            var line = 1;

            foreach (var product in products)
            {
                var reference = Text(product, "productReference");
                var info = reference is not null && _products.TryGetValue(reference, out var found) ? found : null;
                var price = Money(product, "value");

                items.Add(new CollectedOrderItem
                {
                    LineNo = line++,
                    ProductName = info?.Name,
                    SkuRef = reference,
                    Size = Text(product, "productSize"),
                    // O sinal vem da origem: numa troca, a venda tem itens negativos (as peças que
                    // voltaram) e a devolução já traz os itens negativos. Só o cancelamento é
                    // ajustado, porque o detalhe dele repete os valores positivos da venda.
                    Price = kind == OrderKind.Cancellation ? -Math.Abs(price) : price,
                    ImageUrl = info?.ImageUrl
                });
            }

            var customer = clientId is not null && customers.TryGetValue(clientId, out var c) ? c : default;

            return new CollectedOrder
            {
                OrderNumber = orderId,
                OrderDate = orderDate,
                Kind = kind,
                Channel = Text(entry, "channel"),
                Origin = OrderOrigin.Cota,
                Status = Label(Text(entry, "ultimo_status")),
                Total = total,
                OriginalSaleOn = kind == OrderKind.Sale ? null : originalSale,
                CancelledOn = kind == OrderKind.Cancellation ? orderDate : null,
                ExternalId = Text(entry, "idPacote"),
                CustomerName = customer.Name ?? string.Empty,
                CpfDigits = DigitsOnly(customer.Cpf ?? clientId),
                Items = items
            };
        }

        /// <summary>Nome e foto das referências ainda não vistas na sessão (<c>order-detail-info</c>).</summary>
        private async Task LoadProductInfoAsync(
            List<JsonElement> products,
            string? clientId,
            CancellationToken cancellationToken)
        {
            var missing = products
                .Select(p => Text(p, "productReference"))
                .Where(r => r is not null && !_products.ContainsKey(r))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (missing.Count == 0)
            {
                return;
            }

            var query = $"brandId={Uri.EscapeDataString(_brands!)}&clientCpf={Uri.EscapeDataString(clientId ?? string.Empty)}&" +
                string.Join('&', missing.Select(r => "refIds[]=" + Uri.EscapeDataString(r!)));

            using var info = await GetJsonAsync($"{PRODUCT_INFO_PATH}?{query}", cancellationToken);

            if (info.RootElement.TryGetProperty("products", out var array) && array.ValueKind == JsonValueKind.Array)
            {
                foreach (var product in array.EnumerateArray())
                {
                    if (Text(product, "productReference") is { } reference)
                    {
                        _products[reference] = new ProductInfo(
                            Text(product, "name"),
                            LargerImage(Text(product, "imageUrl")));
                    }
                }
            }

            // Referência sem cadastro na loja: lembra a ausência para não perguntar de novo.
            foreach (var reference in missing)
            {
                _products.TryAdd(reference!, new ProductInfo(null, null));
            }
        }

        // -------------------------------------------------------------- parsing

        private static OrderKind KindOf(JsonElement entry)
        {
            if (string.Equals(Text(entry, "status_evento"), CANCELLED_EVENT, StringComparison.OrdinalIgnoreCase))
            {
                return OrderKind.Cancellation;
            }

            return string.Equals(Text(entry, "channel"), RETURN_CHANNEL, StringComparison.OrdinalIgnoreCase)
                ? OrderKind.Return
                : OrderKind.Sale;
        }

        /// <summary>Miniatura 55×55 da VTEX trocada por um tamanho legível na tela.</summary>
        private static string? LargerImage(string? url) =>
            url is null ? null : VtexSizeSegment.Replace(url, $"$1{IMAGE_SIZE_SEGMENT}/", 1);

        /// <summary>"FATURADO" → "Faturado": o status como a tela do Ambrosia exibe.</summary>
        private static string? Label(string? status) =>
            string.IsNullOrWhiteSpace(status) ? null : PtBr.TextInfo.ToTitleCase(status.Trim().ToLower(PtBr));

        private static string? Text(JsonElement element, string name)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
            {
                return null;
            }

            var text = value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                _ => null
            };

            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }

        /// <summary>Valor monetário: a origem manda float (<c>-1265.5899658203125</c>); arredonda a centavos.</summary>
        private static decimal Money(JsonElement element, string name)
        {
            if (element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty(name, out var value)
                && value.ValueKind == JsonValueKind.Number
                && value.TryGetDouble(out var number))
            {
                return Math.Round((decimal)number, 2, MidpointRounding.AwayFromZero);
            }

            return 0m;
        }

        /// <summary>Instante UTC de um campo ISO-8601 (<c>2026-10-26T19:42:23Z</c>).</summary>
        private static DateTime? Instant(JsonElement element, string name) =>
            Text(element, name) is { } text
            && DateTimeOffset.TryParse(
                text,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal,
                out var parsed)
                ? parsed.UtcDateTime
                : null;

        private static string DigitsOnly(string? value) =>
            value is null ? string.Empty : new string(value.Where(char.IsDigit).ToArray());

        // -------------------------------------------------------------- navegação

        /// <summary>Navegação sempre pela allowlist, com rota e tempo no log (FR-042/FR-052).</summary>
        private async Task NavigateAsync(string url, CancellationToken cancellationToken)
        {
            _guard.EnsureAllowed(url);

            var startedAt = DateTime.UtcNow;
            IResponse? response;

            try
            {
                response = await _page.GotoAsync(url, new() { WaitUntil = WaitUntilState.NetworkIdle });
            }
            catch (PlaywrightException ex)
            {
                throw new MaisVendasSourceException("a navegação não completou", ex);
            }

            _logger.LogInformation(
                "Coleta: {Route} respondeu {Status} em {Elapsed:0} ms.",
                NavigationGuard.Describe(url),
                response?.Status,
                (DateTime.UtcNow - startedAt).TotalMilliseconds);

            if (response is not null && response.Status >= 400)
            {
                throw new MaisVendasSourceException($"a origem respondeu {response.Status}");
            }

            cancellationToken.ThrowIfCancellationRequested();
        }

        /// <summary>Captcha ou campo de código à vista = etapa não suportada (FR-046).</summary>
        private async Task<bool> HasUnsupportedStepAsync()
        {
            var selectors = new[]
            {
                "iframe[src*='captcha' i]",
                "[class*='g-recaptcha']",
                "input[name*='otp' i]",
                "input[autocomplete='one-time-code']"
            };

            foreach (var selector in selectors)
            {
                if (await _page.Locator(selector).CountAsync() > 0)
                {
                    return true;
                }
            }

            return false;
        }

        private async Task<bool> WaitForPostLoginAsync(CancellationToken cancellationToken)
        {
            try
            {
                // Fora da tela de login = sessão aberta. A rota inicial depende do perfil da conta
                // (cotaPiloto para vendedora), então o teste é sair de /login.
                await _page.WaitForURLAsync(
                    url => !url.Contains("/login", StringComparison.OrdinalIgnoreCase),
                    new() { Timeout = NAVIGATION_TIMEOUT_MS });

                return true;
            }
            catch (TimeoutException)
            {
                // Ficou na tela de login: senha recusada (FR-007).
                return false;
            }
            finally
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        private void EnsureOpen()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(
                    nameof(PlaywrightSession), "A sessão de coleta já foi encerrada.");
            }
        }

        /// <summary>Nome e foto de uma referência de produto, como a loja os cadastra.</summary>
        private sealed record ProductInfo(string? Name, string? ImageUrl);
    }
}
