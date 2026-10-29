using System.ComponentModel;
using System.Globalization;
using Ambrosio.Domain.Exceptions;
using Ambrosio.Domain.Interfaces;
using Ambrosio.DTO.Enums;
using Ambrosio.DTO.Imports;
using Ambrosio.DTO.Settings;
using Ambrosio.McpServer.Mcp.Attributes;
using Ambrosio.McpServer.Mcp.Results;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Ambrosio.McpServer.Mcp.Tools;

/// <summary>
/// Importação do +Vendas pelo plugin (feature 007, T068; <b>reescrita pela feature 008</b>, research
/// R9/R10, contracts/mcp-tools.md). A conversa pede o período e confirma; a execução é iniciada de
/// verdade, com as credenciais <b>guardadas</b> da vendedora — que nunca passam pelo ChatGPT.
/// <para>
/// O que mudou: <c>iniciar_importacao</c> chamava <c>IImportRunService.StartAsync</c> e só existia
/// como link para a tela, porque a senha precisava ser digitada lá. Com a credencial cifrada em
/// <c>mais_vendas_accounts</c> (FR-001/FR-002 da 008), a tool inicia e devolve o número da execução, e
/// entrou <c>atualizar_base</c> para o período calculado. Nenhuma delas aceita <c>login</c>,
/// <c>senha</c> ou <c>password</c> — o schema é fechado, e a recusa vem dessa ausência (FR-018).
/// </para>
/// <para>
/// Este host não coleta nada: a imagem <c>mcp</c> não carrega Chromium e o worker daqui é removido no
/// <c>Program.cs</c>. <c>StartAsync</c> grava a execução como <c>queued</c> no banco, e o worker da API
/// é quem a reivindica (R9). É por isso também que as tools podem ser chamadas em paralelo com a tela:
/// a fila é o banco, e o índice único por vendedora continua valendo.
/// </para>
/// </summary>
[McpServerToolType]
public sealed class ImportTools
{
    private const string PasswordNotice =
        " Nunca peça nem repasse a senha do +Vendas na conversa: ela é cadastrada na tela de " +
        "configuração do Ambrosio e as tools não têm campo de credencial.";

    /// <summary>Rota da tela de configuração, fixa no frontend (contracts/ui.md).</summary>
    private const string SETTINGS_PATH = "/configuracoes/mais-vendas";

    [McpServerTool(Name = "sugestao_periodo_importacao", Title = "Período sugerido para importar", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description(
        "Sugere o período da próxima importação do +Vendas (continua de onde a última parou), o limite " +
        "de meses, o tamanho dos blocos, se há credenciais cadastradas e se o 'Atualizar base' está " +
        "disponível agora.")]
    [AmbrosioScopes("imports:read")]
    [AmbrosioEndpoint("GET /imports/mais-vendas/defaults")]
    public static Task<CallToolResult> SugestaoPeriodoImportacao(McpUserContext user, IImportRunService imports) =>
        ToolResultFactory.GuardAsync(async () =>
        {
            var defaults = await imports.GetDefaultsAsync(user.RequireUserId());

            return ToolResultFactory.Success(
                new
                {
                    inicioSugerido = ToolMapping.IsoDate(defaults.SuggestedStartDate),
                    fimSugerido = ToolMapping.IsoDate(defaults.SuggestedEndDate),
                    diasPorBloco = defaults.BlockDays,
                    maxMeses = defaults.MaxRangeMonths,
                    temCredenciais = defaults.HasCredentials,
                    situacaoCredenciais = defaults.CredentialsStatus.ToString().ToLowerInvariant(),
                    atualizarBase = new
                    {
                        disponivel = defaults.UpdateBase.Available,
                        inicio = ToolMapping.IsoDate(defaults.UpdateBase.StartDate),
                        fim = ToolMapping.IsoDate(defaults.UpdateBase.EndDate),
                        motivo = defaults.UpdateBase.Reason
                    },
                    bloqueadoAte = defaults.LockedUntil?.ToString("O", CultureInfo.InvariantCulture)
                },
                TextOf(defaults));
        });

    [McpServerTool(Name = "iniciar_importacao", Title = "Importar do +Vendas", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description(
        "Inicia uma importação de pedidos do +Vendas no período pedido, com as credenciais guardadas da " +
        "vendedora: confere as datas com as mesmas regras da tela (limite de meses, data final não " +
        "futura) e, depois da confirmação, cria a execução e devolve o número dela. Não aceita login nem " +
        "senha. Chame primeiro com confirmar=false para mostrar a prévia e com confirmar=true para " +
        "iniciar." + PasswordNotice)]
    [AmbrosioScopes("imports:write")]
    [AmbrosioEndpoint("POST /imports/mais-vendas/runs")]
    public static Task<CallToolResult> IniciarImportacao(
        McpUserContext user,
        IImportRunService imports,
        IValidator<ImportRunStartInfo> validator,
        IOptions<McpSettings> mcp,
        WriteToolHelper writer,
        [Description("Data inicial, AAAA-MM-DD.")] string inicio,
        [Description("Data final, AAAA-MM-DD (não pode ser futura).")] string fim,
        [Description("true = simulação (lê o +Vendas e mostra o que mudaria, sem gravar).")] bool simular = false,
        [Description("false = só a prévia do período; true = inicia a importação.")] bool confirmar = false)
    {
        return writer.RunAsync(
            confirmar,
            preview: async () =>
            {
                var (info, defaults) = await BuildStartAsync(imports, validator, mcp, user, inicio, fim, simular);

                return (
                    new
                    {
                        inicio = ToolMapping.IsoDate(info.StartDate),
                        fim = ToolMapping.IsoDate(info.EndDate),
                        simular = info.DryRun
                    },
                    $"{PeriodText(info.StartDate, info.EndDate, info.DryRun)} {CredentialsWarning(defaults)}".Trim());
            },
            execute: async () =>
            {
                var (info, _) = await BuildStartAsync(imports, validator, mcp, user, inicio, fim, simular);

                var run = await imports.StartAsync(user.RequireUserId(), info, ImportRunStartedVia.Mcp);

                return (
                    new
                    {
                        importacao_id = run.Id,
                        inicio = ToolMapping.IsoDate(run.StartDate),
                        fim = ToolMapping.IsoDate(run.EndDate),
                        simular = run.Mode == ImportRunMode.DryRun,
                        blocosTotal = run.BlocksTotal
                    },
                    $"Importação {run.Id} iniciada: {PeriodText(run.StartDate, run.EndDate, run.Mode == ImportRunMode.DryRun)} " +
                    $"{run.BlocksTotal} bloco(s). Pergunte por 'importacao_atual' para acompanhar.");
            });
    }

    [McpServerTool(Name = "atualizar_base", Title = "Atualizar a base do +Vendas", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description(
        "Atualiza a base: continua de onde a última importação parou, do último pedido importado (inclusive) " +
        "até hoje, sem que a vendedora escolha datas. Confere se está disponível e, depois da " +
        "confirmação, cria a execução e devolve o número dela. Não aceita login, senha nem período — as " +
        "datas são calculadas no servidor." + PasswordNotice)]
    [AmbrosioScopes("imports:write")]
    [AmbrosioEndpoint("POST /imports/mais-vendas/runs/update-base")]
    public static Task<CallToolResult> AtualizarBase(
        McpUserContext user,
        IImportRunService imports,
        IOptions<McpSettings> mcp,
        WriteToolHelper writer,
        [Description("true = simulação (lê o +Vendas e mostra o que mudaria, sem gravar).")] bool simular = false,
        [Description("false = só a prévia do período calculado; true = inicia a importação.")] bool confirmar = false)
    {
        return writer.RunAsync(
            confirmar,
            preview: async () =>
            {
                var defaults = await RequireCredentialsAsync(imports, mcp, user);

                var updateBase = defaults.UpdateBase;

                if (!updateBase.Available)
                {
                    throw new ValidationException([
                        new ValidationFailure("startDate", updateBase.Reason ?? "Ainda não há pedidos importados para atualizar.")]);
                }

                return (
                    new
                    {
                        inicio = ToolMapping.IsoDate(updateBase.StartDate),
                        fim = ToolMapping.IsoDate(updateBase.EndDate),
                        simular
                    },
                    $"{PeriodText(updateBase.StartDate!.Value, updateBase.EndDate!.Value, simular)} " +
                    $"{CredentialsWarning(defaults)}".Trim());
            },
            execute: async () =>
            {
                // A confirmação passa pelo mesmo guard da prévia: sem credenciais cadastradas a recusa
                // tem de sair com o link da tela (contracts/mcp-tools.md), e não com a frase da API, que
                // pressupõe que a vendedora já está numa tela do Ambrosio. `UpdateBaseAsync` também
                // confere — se alguém remover esta linha, a tool continua recusada, só sem o link.
                await RequireCredentialsAsync(imports, mcp, user);

                var run = await imports.UpdateBaseAsync(user.RequireUserId(), simular, ImportRunStartedVia.Mcp);

                return (
                    new
                    {
                        importacao_id = run.Id,
                        inicio = ToolMapping.IsoDate(run.StartDate),
                        fim = ToolMapping.IsoDate(run.EndDate),
                        simular = run.Mode == ImportRunMode.DryRun,
                        blocosTotal = run.BlocksTotal
                    },
                    $"Atualização da base iniciada (importação {run.Id}): de {ToolResultFactory.Date(run.StartDate)} a " +
                    $"{ToolResultFactory.Date(run.EndDate)}, {run.BlocksTotal} bloco(s).");
            });
    }

    [McpServerTool(Name = "importacao_atual", Title = "Importação em andamento", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Mostra a importação do +Vendas em andamento, com o progresso por bloco, ou avisa que não há nenhuma.")]
    [AmbrosioScopes("imports:read")]
    [AmbrosioEndpoint("GET /imports/mais-vendas/runs/current")]
    public static Task<CallToolResult> ImportacaoAtual(McpUserContext user, IImportRunService imports) =>
        ToolResultFactory.GuardAsync(async () =>
        {
            var run = await imports.GetCurrentAsync(user.RequireUserId());

            return run is null
                ? ToolResultFactory.Success(new { emAndamento = false }, "Nenhuma importação em andamento.")
                : ToolResultFactory.Success(new { emAndamento = true, importacao = MapDetail(run) }, RunText(run));
        });

    [McpServerTool(Name = "obter_importacao", Title = "Relatório da importação", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description(
        "Relatório de uma importação do +Vendas: situação, contagens (inseridos, atualizados, sem " +
        "mudança, falhas), blocos, alterações e erros.")]
    [AmbrosioScopes("imports:read")]
    [AmbrosioEndpoint("GET /imports/mais-vendas/runs/{runId}")]
    public static Task<CallToolResult> ObterImportacao(
        McpUserContext user,
        IImportRunService imports,
        [Description("Id da importação.")] long importacao_id) =>
        ToolResultFactory.GuardAsync(async () =>
        {
            var run = await imports.GetByIdAsync(user.RequireUserId(), importacao_id)
                ?? throw new NotFoundException("Importação não encontrada.");

            return ToolResultFactory.Success(MapDetail(run), RunText(run));
        });

    [McpServerTool(Name = "listar_importacoes", Title = "Histórico de importações", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Histórico paginado das importações do +Vendas, mais recentes primeiro.")]
    [AmbrosioScopes("imports:read")]
    [AmbrosioEndpoint("GET /imports/mais-vendas/runs")]
    public static Task<CallToolResult> ListarImportacoes(
        McpUserContext user,
        IImportRunService imports,
        [Description("Página, a partir de 1.")] int pagina = 1,
        [Description("Itens por página, de 1 a 100.")] int tamanho = 20) =>
        ToolResultFactory.GuardAsync(async () =>
        {
            var result = await imports.ListAsync(user.RequireUserId(), new ImportRunListQuery { Page = pagina, PageSize = tamanho });

            return ToolResultFactory.Page(
                result.Items.Select(MapRun),
                result.Page,
                result.PageSize,
                result.TotalCount,
                $"{result.TotalCount} importação(ões); página {result.Page} com {result.Items.Count}.");
        });

    [McpServerTool(Name = "cancelar_importacao", Title = "Cancelar importação", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description(
        "Pede o cancelamento de uma importação em andamento: ela para depois do bloco atual e o que já " +
        "foi gravado fica. Chame primeiro com confirmar=false para ver qual importação será cancelada.")]
    [AmbrosioScopes("imports:write")]
    [AmbrosioEndpoint("POST /imports/mais-vendas/runs/{runId}/cancel")]
    public static Task<CallToolResult> CancelarImportacao(
        McpUserContext user,
        IImportRunService imports,
        WriteToolHelper writer,
        [Description("Id da importação.")] long importacao_id,
        [Description("false = só prévia; true = pede o cancelamento.")] bool confirmar = false)
    {
        return writer.RunAsync(
            confirmar,
            preview: async () =>
            {
                var run = await imports.GetByIdAsync(user.RequireUserId(), importacao_id)
                    ?? throw new NotFoundException("Importação não encontrada.");

                return (MapRun(run), $"Cancelar a importação {run.Id} ({RunText(run)})");
            },
            execute: async () =>
            {
                var run = await imports.CancelAsync(user.RequireUserId(), importacao_id);

                return (MapDetail(run), $"Cancelamento pedido para a importação {run.Id}: ela para depois do bloco atual.");
            });
    }

    // ---------------------------------------------------------------------------------- guarda comum

    /// <summary>
    /// Lê os defaults e exige credenciais cadastradas (FR-004/FR-018 da 008). Sem elas, a recusa é o
    /// erro de domínio com o link da tela de configuração — a única forma de resolver, porque nenhuma
    /// tool deste plugin toca em credencial.
    /// </summary>
    private static async Task<ImportDefaultsInfo> RequireCredentialsAsync(
        IImportRunService imports,
        IOptions<McpSettings> mcp,
        McpUserContext user)
    {
        var defaults = await imports.GetDefaultsAsync(user.RequireUserId());

        if (!defaults.HasCredentials)
        {
            throw new McpCredentialsNotConfiguredException(SettingsLink(mcp));
        }

        return defaults;
    }

    /// <summary>
    /// Monta o pedido de início com as <b>mesmas</b> regras da tela e do <c>POST /runs</c>: datas e
    /// limite de meses no validator do servidor, bloqueio por recusas e execução ativa conferidos antes
    /// de criar qualquer linha (contrato: a prévia valida sem criar).
    /// </summary>
    private static async Task<(ImportRunStartInfo Info, ImportDefaultsInfo Defaults)> BuildStartAsync(
        IImportRunService imports,
        IValidator<ImportRunStartInfo> validator,
        IOptions<McpSettings> mcp,
        McpUserContext user,
        string inicio,
        string fim,
        bool simular)
    {
        var defaults = await RequireCredentialsAsync(imports, mcp, user);

        var from = ToolMapping.ParseDate(inicio, "inicio") ?? throw ToolMapping.Invalid("inicio", "Informe a data inicial do período.");
        var to = ToolMapping.ParseDate(fim, "fim") ?? throw ToolMapping.Invalid("fim", "Informe a data final do período.");

        var info = new ImportRunStartInfo { StartDate = from, EndDate = to, DryRun = simular };

        await validator.ValidateAndThrowAsync(info);

        if (defaults.LockedUntil is { } lockedUntil)
        {
            // FR-008a da 006, valendo igual pelo plugin: a recusa não depende de como o pedido chegou.
            throw new ImportLockedException(lockedUntil);
        }

        if (await imports.GetCurrentAsync(user.RequireUserId()) is not null)
        {
            // Mesmo texto do índice único parcial (ImportRunRepository.ACTIVE_RUN_MESSAGE): a vendedora
            // já tem uma importação rodando, aberta pela tela ou por aqui.
            throw new DuplicateException("Já existe importação em andamento.");
        }

        return (info, defaults);
    }

    private static string SettingsLink(IOptions<McpSettings> mcp) =>
        $"{mcp.Value.FrontendBaseUrl.TrimEnd('/')}{SETTINGS_PATH}";

    private static string TextOf(ImportDefaultsInfo defaults) =>
        defaults.SuggestedStartDate is { } start
            ? $"Sugestão: de {ToolResultFactory.Date(start)} a {ToolResultFactory.Date(defaults.SuggestedEndDate)}; limite de {defaults.MaxRangeMonths} meses."
            : $"Primeira importação: escolha a data inicial; a final sugerida é {ToolResultFactory.Date(defaults.SuggestedEndDate)}.";

    /// <summary>
    /// Aviso de "precisam ser atualizadas" (FR-007): orienta, mas <b>não impede</b> — o +Vendas pode
    /// voltar a aceitar a mesma senha, e travar a importação por aqui seria mais rígido que a tela.
    /// </summary>
    private static string CredentialsWarning(ImportDefaultsInfo defaults) =>
        defaults.CredentialsStatus == MaisVendasCredentialsStatus.NeedsUpdate
            ? "As credenciais salvas foram recusadas na última importação: confira a senha na tela de configuração."
            : string.Empty;

    private static string PeriodText(DateOnly from, DateOnly to, bool simulate) =>
        $"Importar de {ToolResultFactory.Date(from)} a {ToolResultFactory.Date(to)}" + (simulate ? " em simulação." : ".");

    private static string RunText(ImportRunInfo run) =>
        $"Importação {run.Id} {StatusName(run.Status)}: de {ToolResultFactory.Date(run.StartDate)} a " +
        $"{ToolResultFactory.Date(run.EndDate)}, {run.BlocksFinished} de {run.BlocksTotal} bloco(s); " +
        $"{run.Counts.Inserted} inserido(s), {run.Counts.Updated} atualizado(s), {run.Counts.Failed} falha(s).";

    private static string StatusName(ImportRunStatus status) => status switch
    {
        ImportRunStatus.Queued => "na fila",
        ImportRunStatus.Running => "em andamento",
        ImportRunStatus.Succeeded => "concluída",
        ImportRunStatus.Failed => "com falha",
        ImportRunStatus.Partial => "concluída em parte",
        ImportRunStatus.Cancelled => "cancelada",
        _ => status.ToString().ToLowerInvariant()
    };

    private static object MapRun(ImportRunInfo run) => new
    {
        id = run.Id,
        simulacao = run.Mode == ImportRunMode.DryRun,
        // Origem e tipo do pedido (R8 da 008): a vendedora pergunta "quem iniciou isso?" na conversa.
        iniciadaPor = run.StartedVia.ToString().ToLowerInvariant(),
        tipo = run.Kind.ToString().ToLowerInvariant(),
        situacao = StatusName(run.Status),
        inicio = ToolMapping.IsoDate(run.StartDate),
        fim = ToolMapping.IsoDate(run.EndDate),
        blocosTotal = run.BlocksTotal,
        blocosConcluidos = run.BlocksFinished,
        blocoAtual = run.CurrentBlockNo,
        cancelamentoPedido = run.CancelRequested,
        pedidaEm = run.RequestedAt.ToString("O", CultureInfo.InvariantCulture),
        terminadaEm = run.FinishedAt?.ToString("O", CultureInfo.InvariantCulture),
        contagens = run.Counts,
        erro = run.ErrorMessage
    };

    private static object MapDetail(ImportRunDetailInfo run) => new
    {
        importacao = MapRun(run),
        blocos = run.Blocks.Select(b => new
        {
            bloco = b.BlockNo,
            inicio = ToolMapping.IsoDate(b.StartDate),
            fim = ToolMapping.IsoDate(b.EndDate),
            situacao = b.Status.ToString().ToLowerInvariant(),
            contagens = b.Counts,
            erro = b.ErrorMessage
        }).Take(ToolResultFactory.MaxItems).ToList(),
        alteracoes = run.Changes.Take(ToolResultFactory.MaxItems).ToList(),
        totalAlteracoes = run.Changes.Count,
        avisoQuedaDePedidos = run.CountDropWarning
    };
}
