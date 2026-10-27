namespace Ambrosia.DTO.Enums;

/// <summary>
/// Estado de uma execução de importação (feature 006). Persistido como <c>integer</c> com
/// <c>DEFAULT 1</c>; no JSON em camelCase. As transições estão no data-model §4 e o resultado final
/// obedece à FR-047a (research §18).
/// </summary>
public enum ImportRunStatus
{
    /// <summary>Na fila, aguardando vaga em <c>MaxConcurrentRuns</c> (FR-017 — "Aguardando" na tela).</summary>
    Queued = 1,

    /// <summary>Em execução.</summary>
    Running = 2,

    /// <summary>Todos os blocos terminaram sem erro, conflito ou imagem com erro.</summary>
    Succeeded = 3,

    /// <summary>Bloco falho, credenciais recusadas, conta diferente, etapa não suportada ou interrupção.</summary>
    Failed = 4,

    /// <summary>Todos os blocos terminaram, mas houve pedido com erro, conflito ou imagem com erro (FR-047a).</summary>
    Partial = 5,

    /// <summary>Cancelada pela vendedora; encerra ao fim do bloco em curso (FR-019).</summary>
    Cancelled = 6
}
