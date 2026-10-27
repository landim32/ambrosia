using Ambrosia.Domain.Exceptions;

namespace Ambrosia.Infra.AppServices.MaisVendas;

/// <summary>
/// Allowlist da coleta (feature 006, FR-042/FR-052, SC-012): o navegador só visita hosts e rotas do
/// +Vendas que interesam à leitura, e nada além disso.
/// <para>
/// O +Vendas é um sistema de terceiros no qual a vendedora também compra e fecha pedidos. Um coletor
/// que navega livremente pode, com um seletor errado, clicar em "Novo carrinho" ou em "Sair", ou ainda
/// enviar dados a um analytics. A allowlist é a barreira estrutural: quem não está aqui é abortado, e
/// cada URL aceita é registrada no log com o horário — assim a revisão de produção consegue provar o
/// que foi visitado (quickstart §7).
/// </para>
/// <para>
/// Bloqueios explícitos: <c>somabox.somalabs.com.br</c> (o BOX, outra conta da Somalabs), analytics
/// (Google Analytics, Clarity) e qualquer host fora dos permitidos.
/// </para>
/// <para>
/// Três regras: <see cref="IsAllowed"/> para navegação (só as rotas da coleta),
/// <see cref="IsAllowedResource"/> para o que a página carrega (JS, CSS e as APIs do +Vendas) e
/// <see cref="IsAllowedImage"/> para as fotos dos produtos, que ficam no CDN da marca.
/// </para>
/// </summary>
public class NavigationGuard
{
    /// <summary>Host da aplicação do +Vendas.</summary>
    public const string APP_HOST = "maisvendas.somalabs.com.br";

    /// <summary>Host da API interna da mesma aplicação (pedidos, clientes e itens).</summary>
    public const string API_HOST = "api.mais-vendas.somalabs.com.br";

    /// <summary>Segundo host de API da SPA (menu e conferência de vendedora ativa), só leitura.</summary>
    public const string SECONDARY_API_HOST = "mais-vendas-api.somalabs.com.br";

    /// <summary>
    /// Sufixo do CDN da VTEX, onde a loja publica as fotos dos produtos
    /// (ex.: <c>lojacrisbarros.vteximg.com.br</c>). Aceito só para baixar foto, nunca para navegar.
    /// </summary>
    public const string IMAGE_HOST_SUFFIX = ".vteximg.com.br";

    /// <summary>Prefixo das rotas de navegação permitidas (research §9).</summary>
    private static readonly string[] AllowedRoutePrefixes =
    [
        "/login",
        "/cotaPiloto",
        "/storeOmni",
        "/detailOrder"
    ];

    /// <summary>Substrings de host/rota que devem ser abortadas por serem de rastreamento.</summary>
    private static readonly string[] BlockedAnalyticsHosts =
    [
        "google-analytics.com",
        "googletagmanager.com",
        "doubleclick.net",
        "clarity.ms",
        "facebook.net",
        "hotjar.com",
        "mxpnl.com",
        "mixpanel.com"
    ];

    /// <summary>Host bloqueado por não ser o da coleta, ainda que seja da Somalabs.</summary>
    private static readonly string[] BlockedHosts = ["somabox.somalabs.com.br"];

    public NavigationGuard(string baseUrl)
    {
        BaseUri = new Uri(baseUrl, UriKind.Absolute);
        AllowedHosts = [BaseUri.Host, API_HOST, SECONDARY_API_HOST];
    }

    /// <summary>Origem configurada da coleta.</summary>
    public Uri BaseUri { get; }

    /// <summary>Hosts aceitos: o da origem configurada e os das duas APIs do +Vendas.</summary>
    public IReadOnlyList<string> AllowedHosts { get; }

    /// <summary><c>true</c> quando a URL pode ser requisitada/navegada.</summary>
    public bool IsAllowed(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (BlockedAnalyticsHosts.Any(b => uri.Host.Contains(b, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (BlockedHosts.Any(b => string.Equals(uri.Host, b, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        // Só https: a origem é servida com TLS e descer para http exporia a sessão da vendedora.
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!AllowedHosts.Any(h => string.Equals(uri.Host, h, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        // As APIs são liberadas por host; as rotas da aplicação SPA são restringidas ao prefixo que a
        // coleta realmente usa.
        if (string.Equals(uri.Host, API_HOST, StringComparison.OrdinalIgnoreCase)
            || string.Equals(uri.Host, SECONDARY_API_HOST, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IsAllowedRoute(uri.AbsolutePath);
    }

    /// <summary>
    /// Recurso carregado pela página (script, estilo, fonte, XHR): basta ser https num host permitido.
    /// As rotas do app não se aplicam aqui — os arquivos da SPA ficam em <c>/static/</c>.
    /// </summary>
    public bool IsAllowedResource(string url) =>
        TryParseSafe(url, out var uri)
        && AllowedHosts.Any(h => string.Equals(uri.Host, h, StringComparison.OrdinalIgnoreCase));

    /// <summary>Foto de produto: https num host permitido ou no CDN da VTEX.</summary>
    public bool IsAllowedImage(string url) =>
        TryParseSafe(url, out var uri)
        && (AllowedHosts.Any(h => string.Equals(uri.Host, h, StringComparison.OrdinalIgnoreCase))
            || uri.Host.EndsWith(IMAGE_HOST_SUFFIX, StringComparison.OrdinalIgnoreCase));

    /// <summary>URL absoluta, https e fora dos bloqueios explícitos (analytics, BOX).</summary>
    private static bool TryParseSafe(string url, out Uri uri)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out uri!)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var host = uri.Host;

        return !BlockedAnalyticsHosts.Any(b => host.Contains(b, StringComparison.OrdinalIgnoreCase))
            && !BlockedHosts.Any(b => string.Equals(host, b, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Conferência de rota: registra o que passou e lança para o que não passou. Chamada em toda
    /// navegação, com o motivo indo para o log (com rota e tempo — FR-052) e nunca para o banco.
    /// </summary>
    /// <exception cref="MaisVendasSourceException">A URL não está na allowlist — detalhe só com host e rota.</exception>
    public void EnsureAllowed(string url)
    {
        if (IsAllowed(url))
        {
            LastAllowedUrl = url;
            VisitedCount++;

            return;
        }

        var safe = Describe(url);

        throw new MaisVendasSourceException($"rota fora da allowlist: {safe}");
    }

    /// <summary>Descrição curta e segura da URL para log: host + rota, sem query (query pode ter token).</summary>
    public static string Describe(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return "url inválida";
        }

        var path = uri.AbsolutePath.Length > 96 ? uri.AbsolutePath[..96] : uri.AbsolutePath;

        return $"{uri.Host}{path}";
    }

    private static bool IsAllowedRoute(string path) =>
        AllowedRoutePrefixes.Any(prefix =>
            path.Equals(prefix, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(prefix + "?", StringComparison.OrdinalIgnoreCase));

    /// <summary>Última URL aceita e quantas passaram: para o log de fim de execução (SC-012).</summary>
    public string? LastAllowedUrl { get; private set; }

    public int VisitedCount { get; private set; }
}
