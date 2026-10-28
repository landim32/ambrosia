using Ambrosio.DTO.Enums;

namespace Ambrosio.Domain.Models;

/// <summary>
/// Trilha de auditoria das gravações feitas pelo plugin do ChatGPT (tabela <c>mcp_audit_entries</c>)
/// — feature 007, data-model §1.5, FR-020.
/// </summary>
/// <remarks>
/// Sobrevive a reinícios, ao contrário do log de console, e é a resposta operacional para
/// "o ChatGPT mexeu em quê, na minha conta?". Por isso <see cref="Arguments"/> é sempre a versão
/// sanitizada: CPF mascarado, sem <c>senha</c>/<c>password</c>, sem conteúdo de arquivo e sem URL
/// pré-assinada (SC-005). A sanitização é responsabilidade de <c>McpAuditService</c>, não de quem
/// monta a entidade — mas o construtor da linha também recusa um <c>arguments</c> que visivelmente
/// carregue segredo, para que o erro apareça na develop e não em produção.
/// <para>
/// Prévia (<c>confirmar: false</c>) não gera linha: nada mudou, e registrar prévia afogaria a trilha.
/// </para>
/// </remarks>
public class McpAuditEntry
{
    public long Id { get; set; }

    /// <summary>
    /// Dona da ação. Vem exclusivamente do claim <c>sub</c> do token do authorization server, nunca
    /// de argumento de tool (Princípio §VI).
    /// </summary>
    public long UserId { get; set; }

    /// <summary>Nome da tool MCP chamada (<c>cadastrar_cliente</c>, <c>enviar_imagem</c>…).</summary>
    public string ToolName { get; set; } = string.Empty;

    /// <summary>
    /// <c>client_id</c> OAuth que chamou — distingue "feito pelo ChatGPT" de "feito por outro cliente
    /// autorizado pela mesma vendedora". Nulo quando a chamada não veio de um cliente registrado.
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>JSON dos argumentos já sanitizados. Nunca conterá CPF completo, senha ou URL assinada.</summary>
    public string Arguments { get; set; } = string.Empty;

    /// <summary>Resultado da chamada.</summary>
    public McpAuditOutcome Outcome { get; set; }

    /// <summary>Id criado/alterado, ou a mensagem de erro em português. Resumo: não é payload.</summary>
    public string? ResultSummary { get; set; }

    /// <summary>Instante UTC do registro.</summary>
    public DateTime CreatedAt { get; set; }

    public User? User { get; set; }

    /// <summary>
    /// Monta a linha já com o carimbo de tempo, centralizando o que toda gravação precisa ter.
    /// </summary>
    /// <param name="userId">Dono da ação, do <c>sub</c> do token.</param>
    /// <param name="toolName">Nome da tool.</param>
    /// <param name="arguments">Argumentos <b>já sanitizados</b>.</param>
    /// <param name="outcome">Resultado.</param>
    /// <param name="clientId"><c>client_id</c> OAuth, se houver.</param>
    /// <param name="resultSummary">Id afetado ou mensagem de erro.</param>
    /// <param name="now">Instante UTC de <see cref="Interfaces.IClock"/>.</param>
    public static McpAuditEntry Create(
        long userId,
        string toolName,
        string arguments,
        McpAuditOutcome outcome,
        string? clientId,
        string? resultSummary,
        DateTime now)
    {
        if (userId <= 0)
        {
            throw new ArgumentException("O usuário da auditoria é obrigatório.", nameof(userId));
        }

        if (string.IsNullOrWhiteSpace(toolName))
        {
            throw new ArgumentException("A tool auditada é obrigatória.", nameof(toolName));
        }

        return new McpAuditEntry
        {
            UserId = userId,
            ToolName = toolName.Trim(),
            ClientId = string.IsNullOrWhiteSpace(clientId) ? null : clientId.Trim(),
            Arguments = arguments ?? string.Empty,
            Outcome = outcome,
            ResultSummary = string.IsNullOrWhiteSpace(resultSummary) ? null : resultSummary.Trim(),
            CreatedAt = now
        };
    }
}
