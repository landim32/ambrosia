using Ambrosio.DTO.Imports.Collector;

namespace Ambrosio.Infra.Interfaces.AppServices;

/// <summary>
/// Abstração da coleta do +Vendas (feature 006, research §3/§9/§10). É a fronteira entre o Domain e o
/// sistema externo: o Domain só vê <see cref="CollectedBlock"/> e <see cref="CollectedOrder"/>, e não
/// sabe se vieram do navegador, de uma resposta interceptada, do DOM ou do coletor fake de homologação.
/// <para>
/// Duas implementações existem, escolhidas pela configuração <c>MaisVendas:Collector</c> no
/// <c>Startup</c>: <c>Playwright</c> (real, produção/Development) e <c>Fake</c> (determinístico, só
/// homolog — recusado no startup em Production).
/// </para>
/// </summary>
public interface IMaisVendasCollectorAppService
{
    /// <summary>
    /// Versão do coletor, gravada em <c>import_runs.collector_version</c> (data-model §2): faz o
    /// relatório dizer qual mecanismo produziu cada número — essencial enquanto os seletores do
    /// Playwright são ajustados na primeira execução em simulação.
    /// </summary>
    string CollectorVersion { get; }

    /// <summary>
    /// Abre uma sessão de coleta com as credenciais <b>desta</b> execução (FR-044: contexto novo por
    /// execução, sem estado de navegação reaproveitado, sem <c>storageState</c>). O chamador é obrigado
    /// a descartá-la no <c>finally</c>.
    /// </summary>
    Task<IMaisVendasCollectorSession> OpenSessionAsync(MaisVendasCredentials credentials, CancellationToken cancellationToken = default);
}

/// <summary>
/// Sessão de coleta aberta (feature 006). Vive enquanto a execução dura e é fechada no <c>finally</c>,
/// com sucesso, falha ou cancelamento (FR-044) — fechar o navegador é o que derruba cookies e sessão
/// remota do processo.
/// </summary>
public interface IMaisVendasCollectorSession : IAsyncDisposable
{
    /// <summary>
    /// Autentica na origem. Não lança em credencial recusada: devolve o resultado, porque recusa
    /// alimenta a contagem de tentativas e o bloqueio (FR-008a), e etapa não suportada tem mensagem
    /// própria (FR-046).
    /// </summary>
    Task<MaisVendasLoginResult> LoginAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Coleta o intervalo de datas inclusive, com os contadores e totais que a própria origem exibe por
    /// aba (FR-010/FR-011). Uma venda cancelada vira dois registros; devolução entra negativa
    /// (FR-029/FR-031).
    /// </summary>
    /// <exception cref="Ambrosio.Domain.Exceptions.MaisVendasSourceException">Origem indisponível após as retentativas (FR-045).</exception>
    Task<CollectedBlock> CollectBlockAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    /// <summary>
    /// Baixa uma foto da origem <b>com a sessão da coleta</b> (as URLs exigem os cookies da vendedora —
    /// FR-037 e o caso "fotos baixadas com a sessão" da spec), com o tamanho limitado a
    /// <paramref name="maxBytes"/> antes de virar memória.
    /// </summary>
    /// <exception cref="Ambrosio.Domain.Exceptions.MaisVendasSourceException">Download falhou; o item fica sem imagem e o pedido continua (FR-039).</exception>
    Task<CollectedImage> DownloadImageAsync(string url, long maxBytes, CancellationToken cancellationToken = default);
}
