namespace Ambrosio.DTO.Images;

/// <summary>
/// Cabeçalho de um objeto no storage (resultado de <c>HeadObject</c>), usado pela confirmação de
/// envio para conferir o tamanho <b>antes</b> de baixar qualquer byte (research §6, FR-006).
/// </summary>
public class ObjectHead
{
    /// <summary>Tamanho real do objeto em bytes.</summary>
    public long ContentLength { get; set; }

    /// <summary>Tipo gravado no objeto; pode ser nulo em servidores que não o devolvem.</summary>
    public string? ContentType { get; set; }
}
