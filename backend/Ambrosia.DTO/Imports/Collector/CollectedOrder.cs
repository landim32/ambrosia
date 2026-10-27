using Ambrosia.DTO.Enums;

namespace Ambrosia.DTO.Imports.Collector;

/// <summary>
/// Um pedido como a origem o apresenta, já normalizado (feature 006, research §10). É o contrato entre
/// o coletor e o Domain: o que entra aqui é o que vira <c>Order</c>/<c>OrderItem</c> e o que alimenta
/// o <c>source_hash</c> (research §6).
/// <para>
/// Uma <b>venda cancelada na origem</b> produz dois registros deste tipo — a venda positiva e o
/// cancelamento negativo (FR-029) — e o mesmo <see cref="OrderNumber"/> em datas diferentes são dois
/// pedidos distintos (US1 cenário 9), porque a chave natural é quádrupla.
/// </para>
/// </summary>
public sealed record CollectedOrder
{
    /// <summary>Chave natural: número como a origem mostra.</summary>
    public string OrderNumber { get; init; } = string.Empty;

    /// <summary>Chave natural: data do pedido (ano já resolvido — research §10).</summary>
    public DateOnly OrderDate { get; init; }

    /// <summary>Chave natural: venda, devolução ou cancelamento.</summary>
    public OrderKind Kind { get; init; }

    /// <summary>Canal de origem (Linx, Online, …) exibido pela aba.</summary>
    public string? Channel { get; init; }

    /// <summary>Origem/aba que produziu o pedido: cota ou omni.</summary>
    public OrderOrigin? Origin { get; init; }

    /// <summary>Status de origem (Faturado, Cancelado, …).</summary>
    public string? Status { get; init; }

    /// <summary>Total já convertido para decimal de 2 casas (negativo em devolução/cancelamento).</summary>
    public decimal Total { get; init; }

    /// <summary>Cancelamento: data da venda original.</summary>
    public DateOnly? OriginalSaleOn { get; init; }

    /// <summary>Cancelamento: data do cancelamento.</summary>
    public DateOnly? CancelledOn { get; init; }

    /// <summary>Identificador externo (4º segmento da URL de detalhe).</summary>
    public string? ExternalId { get; init; }

    /// <summary>Nome do cliente como a origem o traz; usado para criar/atualizar o cliente (FR-034/FR-035).</summary>
    public string CustomerName { get; init; } = string.Empty;

    /// <summary>
    /// CPF <b>só dígitos</b>, ainda não validado. Nunca é persistido em claro nem vai para log,
    /// mensagem de erro ou rastro: a validação e a proteção (hash/cifra/máscara) acontecem no
    /// Domain/Infra antes da gravação (FR-033, research §7).
    /// </summary>
    public string? CpfDigits { get; init; }

    public List<CollectedOrderItem> Items { get; init; } = [];

    /// <summary>Sem CPF na saída (FR-052): só a chave natural do pedido.</summary>
    public override string ToString() =>
        $"{OrderNumber} {OrderDate:yyyy-MM-dd} {Kind} {Items.Count} itens";
}
