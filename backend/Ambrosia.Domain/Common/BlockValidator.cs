using Ambrosia.DTO.Enums;
using Ambrosia.DTO.Imports.Collector;

namespace Ambrosia.Domain.Common;

/// <summary>
/// Conferência de um bloco coletado <b>antes</b> de qualquer gravação (feature 006, FR-010/FR-011,
/// SC-009, research §10).
/// <para>
/// O +Vendas exibe, em cada aba, quantos pedidos há e qual a soma. Se a lista lida não bate com o que
/// a própria origem anuncia, a lista está incompleta (uma linha não renderizou, um "Ver mais" não
/// clicou, a resposta interceptada veio truncada) — e gravar meia lista produziria exatamente o que a
/// feature existe para evitar: diferença silenciosa entre os dois sistemas. Então o bloco falha, e a
/// vendedora o repete.
/// </para>
/// <para>
/// Dois conferidores por aba/origem: <b>contadores</b> (linhas lidas × quantidade anunciada) e
/// <b>totais</b> (soma dos valores lidos × total anunciado). A regra "Todos" e a regra por tipo são as
/// duas leituras da mesma lista: a origem mostra o total por aba e o total da soma de tudo, e as duas
/// precisam fechar.
/// </para>
/// <para>Regra pura, sem IO. A mensagem devolvida não contém CPF, nome de cliente nem credencial —
/// ela vai direto para <c>import_run_blocks.error_message</c>, visível na tela (FR-052).</para>
/// </summary>
public static class BlockValidator
{
    /// <summary>Tolerância de arredondamento de moeda (a origem exibe 2 casas; somas parciais podem trazer 1 centavo de erro).</summary>
    private const decimal MONEY_TOLERANCE = 0.01m;

    /// <summary>
    /// A divergência encontrada, ou <c>null</c> quando o bloco confere e pode ser gravado.
    /// </summary>
    public static string? FindDivergence(CollectedBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);

        return FindCounterDivergence(block) ?? FindTotalDivergence(block);
    }

    /// <summary>Quantidade de linhas lidas × contador anunciado, por aba de origem (FR-010).</summary>
    private static string? FindCounterDivergence(CollectedBlock block)
    {
        foreach (var origin in new[] { OrderOrigin.Cota, OrderOrigin.Omni })
        {
            var inOrigin = block.Orders.Count(o => o.Origin == origin);
            var key = new CollectorTabKey(origin, CollectorTab.All);

            if (!block.TabCounters.TryGetValue(key, out var announced))
            {
                // A origem não anunciou nada para esta aba: nada há para conferir, e uma aba sem
                // contador no contrato do fake/Playwright significa "não exibida".
                continue;
            }

            if (announced != inOrigin)
            {
                return $"A contagem da aba {origin.ToLowerName()} divergiu: a origem anuncia {announced} pedido(s) e a lista entregou {inOrigin}.";
            }

            // Conferência por tipo dentro da aba: um cancelamento perdido na leitura mudaria o sinal da
            // soma sem mudar a contagem de "Todos", e é justamente o que o relatório esconderia.
            foreach (var (tab, kind) in new[]
            {
                (CollectorTab.Sale, OrderKind.Sale),
                (CollectorTab.Return, OrderKind.Return),
                (CollectorTab.Cancellation, OrderKind.Cancellation)
            })
            {
                var typeKey = new CollectorTabKey(origin, tab);

                if (!block.TabCounters.TryGetValue(typeKey, out var announcedByKind))
                {
                    continue;
                }

                var read = block.Orders.Count(o => o.Origin == origin && o.Kind == kind);

                if (announcedByKind != read)
                {
                    return $"A contagem da aba {tab.ToLowerName()} de {origin.ToLowerName()} divergiu: a origem anuncia {announcedByKind} e a lista entregou {read}.";
                }
            }
        }

        return null;
    }

    /// <summary>Soma dos valores lidos × total anunciado, por aba de origem (FR-011).</summary>
    private static string? FindTotalDivergence(CollectedBlock block)
    {
        foreach (var origin in new[] { OrderOrigin.Cota, OrderOrigin.Omni })
        {
            var key = new CollectorTabKey(origin, CollectorTab.All);

            if (!block.TabTotals.TryGetValue(key, out var announced))
            {
                continue;
            }

            var read = Math.Round(
                block.Orders.Where(o => o.Origin == origin).Sum(o => o.Total),
                2,
                MidpointRounding.AwayFromZero);

            if (Math.Abs(announced - read) > MONEY_TOLERANCE)
            {
                return $"O total da aba {origin.ToLowerName()} divergiu: a origem anuncia {Money(announced)} e a lista soma {Money(read)}.";
            }
        }

        return null;
    }

    private static string Money(decimal value) =>
        string.Format(System.Globalization.CultureInfo.InvariantCulture, "R$ {0:0.00}", value);

    private static string ToLowerName(this Enum value) =>
        value.ToString().ToLowerInvariant();
}
