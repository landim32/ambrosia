namespace Ambrosio.DTO.Settings;

/// <summary>
/// Configuração da importação do +Vendas (feature 006, research §13; feature 008, research R2/R6).
/// Ligada à seção <c>MaisVendas</c> do appsettings e às variáveis <c>MaisVendas__*</c>; consumida via
/// <c>IOptions&lt;MaisVendasSettings&gt;</c> pelo coletor (Infra), pelo <c>ImportRunService</c>
/// (sugestão de datas, limites), pelo <c>ImportExecutionService</c> (ritmo, bloqueio), pelo
/// <c>MaisVendasCredentialService</c> e pela cifra de credenciais (feature 008) e pelo
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

    /// <summary>
    /// Limite do período em <b>meses corridos</b>, inclusivo (faixa 1-12; feature 008, research R6).
    /// Substitui <c>MaxRangeDays</c> da feature 006: "3 meses" em dias variaria entre 89 e 92, e a
    /// conta por mês casa com a percepção da vendedora (26/07 a 25/10 entra, 26/07 a 26/10 não).
    /// </summary>
    public int MaxRangeMonths { get; set; } = 3;

    /// <summary>
    /// Chave de cifra das credenciais do +Vendas: base64 de exatamente 32 bytes (AES-256-GCM,
    /// feature 008, research R2). Obrigatória — API e host do MCP recusam o startup sem ela
    /// (constituição §VI, emenda 3.4.1). Dedicada: não é a chave do CPF, para que um vazamento
    /// não abra as duas coisas juntas. Trocá-la invalida o que já está gravado.
    /// </summary>
    public string CredentialsKey { get; set; } = string.Empty;

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
