using System.ComponentModel;
using System.Globalization;
using Ambrosio.Domain.Exceptions;
using Ambrosio.Domain.Interfaces;
using Ambrosio.DTO.Imports;
using Ambrosio.DTO.Settings;
using Ambrosio.McpServer.Mcp.Attributes;
using Ambrosio.McpServer.Mcp.Results;
using FluentValidation;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Ambrosio.McpServer.Mcp.Tools;

/// <summary>
/// Importação do +Vendas (feature 007, T068, R9). A senha do +Vendas nunca passa pela conversa:
/// <c>iniciar_importacao</c> valida o período e devolve o link da tela de importação, onde a vendedora
/// digita as credenciais. As demais tools acompanham e cancelam execuções pelo
/// <see cref="IImportRunService"/>, o mesmo do controller.
/// </summary>
[McpServerToolType]
public sealed class ImportTools
{
    private const string PasswordNotice =
        " Nunca peça nem repasse a senha do +Vendas na conversa: a vendedora digita a senha na tela do " +
        "Ambrosio, no link devolvido.";

    [McpServerTool(Name = "sugestao_periodo_importacao", Title = "Período sugerido para importar", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description(
        "Sugere o período da próxima importação do +Vendas (continua de onde a última parou), o tamanho " +
        "dos blocos, o limite de dias e se novas importações estão bloqueadas até algum horário.")]
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
                    maxDias = defaults.MaxRangeDays,
                    bloqueadoAte = defaults.LockedUntil?.ToString("O", CultureInfo.InvariantCulture)
                },
                defaults.SuggestedStartDate is { } start
                    ? $"Sugestão: de {ToolResultFactory.Date(start)} a {ToolResultFactory.Date(defaults.SuggestedEndDate)}."
                    : $"Primeira importação: escolha a data inicial; a final sugerida é {ToolResultFactory.Date(defaults.SuggestedEndDate)}.");
        });

    [McpServerTool(Name = "iniciar_importacao", Title = "Importar do +Vendas", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description(
        "Prepara uma importação de pedidos do +Vendas para um período: confere as datas com as mesmas " +
        "regras da tela e devolve o link da tela de importação já com o período, onde a vendedora digita " +
        "login e senha e inicia. Não inicia nada sozinha e não aceita login nem senha. Chame primeiro com " +
        "confirmar=false para mostrar o período e com confirmar=true para receber o link." + PasswordNotice)]
    [AmbrosioScopes("imports:write")]
    [AmbrosioEndpoint("POST /imports/mais-vendas/runs")]
    public static Task<CallToolResult> IniciarImportacao(
        McpUserContext user,
        IValidator<ImportRunStartInfo> validator,
        IOptions<McpSettings> mcp,
        WriteToolHelper writer,
        [Description("Data inicial, AAAA-MM-DD.")] string inicio,
        [Description("Data final, AAAA-MM-DD (não pode ser futura).")] string fim,
        [Description("true = simulação (lê o +Vendas e mostra o que mudaria, sem gravar). Escolhido na tela.")] bool simular = false,
        [Description("false = só confere o período; true = devolve o link da tela.")] bool confirmar = false)
    {
        return writer.RunAsync(
            confirmar,
            preview: async () =>
            {
                var (from, to) = await ValidatePeriodAsync(validator, inicio, fim);

                return (new { inicio = ToolMapping.IsoDate(from), fim = ToolMapping.IsoDate(to), simular }, PeriodText(from, to, simular));
            },
            execute: async () =>
            {
                var (from, to) = await ValidatePeriodAsync(validator, inicio, fim);
                var link = $"{mcp.Value.FrontendBaseUrl.TrimEnd('/')}/importacao?inicio={ToolMapping.IsoDate(from)}&fim={ToolMapping.IsoDate(to)}";

                return (
                    new { link, inicio = ToolMapping.IsoDate(from), fim = ToolMapping.IsoDate(to), simular },
                    $"Abra {link} para digitar o login e a senha do +Vendas e iniciar" +
                    (simular ? " (marque a simulação na tela)." : "."));
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

    /// <summary>
    /// Mesmas regras de data do <c>ImportRunStartInfoValidator</c> (formato, data final não futura,
    /// início ≤ fim, limite de dias). As credenciais do +Vendas não fazem parte desta tool: são
    /// preenchidas com marcadores só para o validator avaliar as datas.
    /// </summary>
    private static async Task<(DateOnly From, DateOnly To)> ValidatePeriodAsync(
        IValidator<ImportRunStartInfo> validator,
        string inicio,
        string fim)
    {
        var from = ToolMapping.ParseDate(inicio, "inicio") ?? throw ToolMapping.Invalid("inicio", "Informe a data inicial do período.");
        var to = ToolMapping.ParseDate(fim, "fim") ?? throw ToolMapping.Invalid("fim", "Informe a data final do período.");

        await validator.ValidateAndThrowAsync(new ImportRunStartInfo
        {
            Login = "-",
            Password = "-",
            StartDate = from,
            EndDate = to
        });

        return (from, to);
    }

    private static string PeriodText(DateOnly from, DateOnly to, bool simulate) =>
        $"Importar de {ToolResultFactory.Date(from)} a {ToolResultFactory.Date(to)}" + (simulate ? " em simulação." : ".");

    private static string RunText(ImportRunInfo run) =>
        $"Importação {run.Id} {StatusName(run.Status)}: de {ToolResultFactory.Date(run.StartDate)} a " +
        $"{ToolResultFactory.Date(run.EndDate)}, {run.BlocksFinished} de {run.BlocksTotal} bloco(s); " +
        $"{run.Counts.Inserted} inserido(s), {run.Counts.Updated} atualizado(s), {run.Counts.Failed} falha(s).";

    private static string StatusName(Ambrosio.DTO.Enums.ImportRunStatus status) => status switch
    {
        Ambrosio.DTO.Enums.ImportRunStatus.Queued => "na fila",
        Ambrosio.DTO.Enums.ImportRunStatus.Running => "em andamento",
        Ambrosio.DTO.Enums.ImportRunStatus.Succeeded => "concluída",
        Ambrosio.DTO.Enums.ImportRunStatus.Failed => "com falha",
        Ambrosio.DTO.Enums.ImportRunStatus.Partial => "concluída em parte",
        Ambrosio.DTO.Enums.ImportRunStatus.Cancelled => "cancelada",
        _ => status.ToString().ToLowerInvariant()
    };

    private static object MapRun(ImportRunInfo run) => new
    {
        id = run.Id,
        simulacao = run.Mode == Ambrosio.DTO.Enums.ImportRunMode.DryRun,
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
