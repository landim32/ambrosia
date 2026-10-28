namespace Ambrosio.DTO.Enums;

/// <summary>
/// Resultado de uma chamada de escrita do plugin do ChatGPT, para a trilha de auditoria
/// (feature 007, data-model §1.5, FR-020). Vai como <c>integer</c> na tabela
/// <c>mcp_audit_entries.outcome</c>, pela regra do Princípio §V.
/// </summary>
/// <remarks>
/// Os quatro valores cobrem o que a FR-020 exige registrar: o que foi gravado e o que foi recusado.
/// Prévia (<c>confirmar: false</c>) <b>não</b> é um resultado auditável e por isso não tem membro
/// aqui — registrar prévia lotaria a trilha de chamadas que não mudaram nada (data-model §1.5,
/// "prévias não são auditadas").
/// <para>
/// Membros começam em 1: o <c>0</c> CLR não é um resultado válido, o que impede uma linha gravada
/// por código esquecido de parecer "recusada por escopo".
/// </para>
/// </remarks>
public enum McpAuditOutcome
{
    /// <summary>Gravação confirmada e concluída.</summary>
    Recorded = 1,

    /// <summary>Recusada por regra de negócio da API (validação, duplicidade, não encontrado).</summary>
    RejectedByRule = 2,

    /// <summary>Recusada por falta de escopo no token — tentativa, não alteração.</summary>
    RejectedByScope = 3,

    /// <summary>Falha técnica inesperada; o detalhe vai em <c>result_summary</c>.</summary>
    UnexpectedError = 4
}
