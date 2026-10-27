namespace Ambrosio.DTO.Enums;

/// <summary>
/// Modo da execução de importação (feature 006). No JSON: <c>dryRun</c> | <c>apply</c>.
/// </summary>
public enum ImportRunMode
{
    /// <summary>
    /// Simulação (FR-032): percorre a origem, aplica as mesmas validações e reporta o que <i>seria</i>
    /// feito, sem gravar pedido, cliente, imagem nem vínculo de conta.
    /// </summary>
    DryRun = 1,

    /// <summary>Grava de verdade (upsert idempotente por chave natural).</summary>
    Apply = 2
}
