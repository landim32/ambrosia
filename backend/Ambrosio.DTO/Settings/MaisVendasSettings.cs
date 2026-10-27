namespace Ambrosio.DTO.Settings;

/// <summary>
/// Configuração da importação do +Vendas (feature 006, research §13). Ligada à seção
/// <c>MaisVendas</c> do appsettings e às variáveis <c>MaisVendas__*</c>; consumida via
/// <c>IOptions&lt;MaisVendasSettings&gt;</c> pelo coletor (Infra), pelo <c>ImportRunService</c>
/// (sugestão de datas, limites), pelo <c>ImportExecutionService</c> (ritmo, bloqueio) e pelo
/// <c>ImportRunWorker</c> (concorrência).
/// <para>
/// As faixas abaixo são conferidas no startup (<c>Program.ValidateSettings</c>): a API não sobe com
/// valor fora delas, nem com <see cref="Collector"/> = <c>Fake</c> em Production — o fake existe só
/// para os testes de API da homologação (research §3).
/// </para>
/// </summary>
public class MaisVendasSettings
{
    /// <summary>Coletor de Playwright real (produção e Development de uso real).</summary>
    public const string COLLECTOR_PLAYWRIGHT = "Playwright";

    /// <summary>Coletor determinístico sem rede, só para homologação (research §3).</summary>
    public const string COLLECTOR_FAKE = "Fake";

    /// <summary>
    /// Qual coletor registrar no <c>Startup</c>: <c>Playwright</c> | <c>Fake</c>. <c>Fake</c> é
    /// recusado em Production.
    /// </summary>
    public string Collector { get; set; } = COLLECTOR_PLAYWRIGHT;

    /// <summary>
    /// Origem da coleta. URL https absoluta; os hosts permitidos da allowlist derivam dela
    /// (research §9) e ela nunca vem de entrada do usuário.
    /// </summary>
    public string BaseUrl { get; set; } = "https://maisvendas.somalabs.com.br";

    /// <summary>Dias por bloco de coleta (faixa 1-31, FR-009).</summary>
    public int BlockDays { get; set; } = 7;

    /// <summary>Tamanho máximo do período em dias, inclusive (faixa 1-3660, FR-004).</summary>
    public int MaxRangeDays { get; set; } = 366;

    /// <summary>Execuções simultâneas por servidor (faixa 1-8, FR-017).</summary>
    public int MaxConcurrentRuns { get; set; } = 2;

    /// <summary>Recusas seguidas de credencial antes do bloqueio (faixa 1-10, FR-008a).</summary>
    public int MaxFailedLogins { get; set; } = 3;

    /// <summary>Duração do bloqueio após a última recusa, em minutos (faixa 1-1440, FR-008a).</summary>
    public int LockoutMinutes { get; set; } = 15;

    /// <summary>Pausa mínima antes de cada ação na origem, em ms (faixa 100-5000, FR-043).</summary>
    public int MinDelayMs { get; set; } = 300;

    /// <summary>Pausa máxima antes de cada ação na origem, em ms; precisa ser ≥ mínima (FR-043).</summary>
    public int MaxDelayMs { get; set; } = 900;

    /// <summary>Configuração do coletor Fake (só homologação; research §3).</summary>
    public FakeCollectorSettings Fake { get; set; } = new();

    /// <summary><c>true</c> quando o coletor registrado é o fake determinístico.</summary>
    public bool IsFake => string.Equals(Collector, COLLECTOR_FAKE, StringComparison.OrdinalIgnoreCase);

    /// <summary><c>true</c> quando o coletor registrado é o Playwright real.</summary>
    public bool IsPlaywright => string.Equals(Collector, COLLECTOR_PLAYWRIGHT, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Ajustes do coletor fake (research §3). O atraso existe para o progresso por bloco ser observável
/// na tela e nos testes sem depender de rede.
/// </summary>
public class FakeCollectorSettings
{
    /// <summary>Pausa entre pedidos coletados, em ms (faixa 0-5000; padrão 50).</summary>
    public int DelayMs { get; set; } = 50;
}
