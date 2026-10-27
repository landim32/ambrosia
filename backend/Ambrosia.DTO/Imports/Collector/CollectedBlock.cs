using Ambrosia.DTO.Enums;

namespace Ambrosia.DTO.Imports.Collector;

/// <summary>
/// Aba da lista do +Vendas cuja contagem e total a origem exibe e o coletor confere (feature 006,
/// FR-010/FR-011). <see cref="All"/> corresponde à aba "Todos".
/// </summary>
public enum CollectorTab
{
    All = 1,
    Sale = 2,
    Return = 3,
    Cancellation = 4
}

/// <summary>
/// Chave de uma aba de contagem: a origem (cota/omni) × a aba. O bloco guarda contadores e totais por
/// par, porque o +Vendas mostra um número para cada aba dentro de cada canal (research §10).
/// </summary>
/// <param name="Origin">Canal de onde o pedido vem.</param>
/// <param name="Tab">Aba exibida.</param>
public readonly record struct CollectorTabKey(OrderOrigin Origin, CollectorTab Tab)
{
    /// <summary>Rótulo curto para log e mensagem de divergência (sem dado pessoal).</summary>
    public override string ToString() => $"{Origin.ToString().ToLowerInvariant()}/{Tab.ToString().ToLowerInvariant()}";
}

/// <summary>
/// Resultado da coleta de <b>um bloco</b> de datas (feature 006, research §10).
/// <para>
/// Além dos pedidos lidos, traz os contadores e os totais que a <b>própria origem</b> exibe por aba.
/// É com esse par que o <c>BlockValidator</c> confere a lista antes de qualquer gravação: se a origem
/// diz "12 pedidos, R$ 8.400,00" e a lista lida tem 11 linhas, o bloco falha sem escrever nada
/// (FR-010/FR-011, SC-009). A estrutura não depende da fonte (resposta interceptada ou DOM), então
/// trocar de fonte não alcança o Domain.
/// </para>
/// </summary>
public sealed class CollectedBlock
{
    public List<CollectedOrder> Orders { get; init; } = [];

    /// <summary>Quantidade de pedidos declarada pela origem, por aba.</summary>
    public Dictionary<CollectorTabKey, int> TabCounters { get; init; } = [];

    /// <summary>Soma dos valores declarada pela origem, por aba.</summary>
    public Dictionary<CollectorTabKey, decimal> TabTotals { get; init; } = [];
}
