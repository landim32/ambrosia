namespace Ambrosio.DTO.Enums;

/// <summary>
/// De onde veio o pedido de uma execução de importação (feature 008, research R8). Persistido como
/// <c>integer</c> com <c>DEFAULT 1</c>; no JSON em camelCase. Execuções antigas ficam com a tela.
/// </summary>
public enum ImportRunStartedVia
{
    /// <summary>Iniciada pela vendedora na tela de importação.</summary>
    Screen = 1,

    /// <summary>
    /// Iniciada pela conversa, numa tool do plugin do ChatGPT (a execução aparece na tela em até 5 s
    /// pelo polling já existente — SC-002).
    /// </summary>
    Mcp = 2
}
