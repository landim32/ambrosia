namespace Ambrosio.DTO.Enums;

/// <summary>
/// O que a execução de importação pediu (feature 008, research R5/R8). Persistido como <c>integer</c>
/// com <c>DEFAULT 1</c>; no JSON em camelCase. Execuções antigas ficam com o período escolhido.
/// </summary>
public enum ImportRunKind
{
    /// <summary>Período escolhido pela vendedora (início e fim informados).</summary>
    Period = 1,

    /// <summary>
    /// "Atualizar base" (FR-011/FR-012): o período é calculado no servidor — do último pedido
    /// importado até hoje — e não vem de quem pediu.
    /// </summary>
    UpdateBase = 2
}
