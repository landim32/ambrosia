namespace Ambrosio.DTO.Enums;

/// <summary>
/// Como o pedido entrou no Ambrosio (feature 006, data-model §3). Persistido como <c>integer</c>
/// com <c>DEFAULT 1</c> — as linhas existentes da feature 001 continuam manuais sem backfill.
/// No JSON: <c>manual</c> | <c>maisVendas</c>.
/// <para>
/// É o que separa o conflito (FR-028) da atualização idempotente: a importação só sobrescreve
/// pedido <see cref="MaisVendas"/>; pedido <see cref="Manual"/> com a mesma chave natural é
/// reportado e fica intacto.
/// </para>
/// </summary>
public enum OrderSource
{
    /// <summary>Cadastrado pela vendedora no Ambrosio.</summary>
    Manual = 1,

    /// <summary>Importado do +Vendas.</summary>
    MaisVendas = 2
}
