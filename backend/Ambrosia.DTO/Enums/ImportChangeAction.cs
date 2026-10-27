namespace Ambrosia.DTO.Enums;

/// <summary>
/// O que a importação decidiu para um pedido (feature 006). É o tipo de linha de
/// <c>import_run_changes</c> e a dimensão do agrupamento do relatório (FR-048/FR-049); no JSON em
/// camelCase.
/// </summary>
public enum ImportChangeAction
{
    /// <summary>Chave natural inexistente: pedido criado.</summary>
    Inserted = 1,

    /// <summary>Existe com hash diferente (ou hash nulo por edição manual): campos da origem aplicados (FR-023).</summary>
    Updated = 2,

    /// <summary>Existe com o mesmo hash: só <c>captured_at</c> mudou (FR-021).</summary>
    Unchanged = 3,

    /// <summary>Existia de outra importação e não apareceu na origem nesta execução; nada foi apagado (FR-027).</summary>
    Missing = 4,

    /// <summary>Existe como pedido manual com a mesma chave natural: não foi tocado (FR-028).</summary>
    Conflict = 5,

    /// <summary>Pedido com dado inválido (ex.: CPF ausente); não interrompe o bloco (FR-036).</summary>
    Error = 6
}
