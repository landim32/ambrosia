namespace Ambrosia.DTO.Enums;

/// <summary>
/// Estado de um bloco de datas da importação (feature 006). Persistido como <c>integer</c> com
/// <c>DEFAULT 1</c>; no JSON em camelCase.
/// </summary>
public enum ImportBlockStatus
{
    /// <summary>Ainda não começado.</summary>
    Pending = 1,

    /// <summary>Coletando/gravando.</summary>
    Running = 2,

    /// <summary>Coletado, validado e gravado.</summary>
    Succeeded = 3,

    /// <summary>Falhou na coleta, na validação dos contadores ou na gravação; a execução para.</summary>
    Failed = 4,

    /// <summary>Não chegou a ser executado porque a execução parou antes (FR-013).</summary>
    NotExecuted = 5
}
