namespace Ambrosia.DTO.Enums;

/// <summary>
/// Estado de vida de uma imagem (data-model). Persistido como <c>integer</c> com default 1; no
/// JSON: <c>pending</c> | <c>available</c> | <c>failed</c> (o conversor camelCase global já faz a
/// serialização como string). Só <see cref="Available"/> expõe URL de leitura (FR-015).
/// </summary>
public enum ImageStatus
{
    /// <summary>Declaração aceita; o binário pode ainda não estar no storage.</summary>
    Pending = 1,

    /// <summary>Binário conferido (tamanho, tipo e hash) ou gravado pelo servidor.</summary>
    Available = 2,

    /// <summary>Recusada na confirmação; o objeto foi apagado. Estado final (FR-007).</summary>
    Failed = 3
}
