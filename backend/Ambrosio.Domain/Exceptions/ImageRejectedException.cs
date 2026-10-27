namespace Ambrosio.Domain.Exceptions;

/// <summary>Motivo pelo qual a confirmação de um envio recusou o arquivo (contrato <c>images-api.md</c>).</summary>
public enum ImageRejectionReason
{
    /// <summary>O objeto declarado ainda não está no storage. O envio continua <c>pending</c>.</summary>
    NotUploaded,

    /// <summary>Tamanho real diferente do declarado, ou acima de <c>MaxUploadBytes</c>.</summary>
    Size,

    /// <summary>Conteúdo que não é imagem de tipo aceito, ou de tipo diverso do declarado.</summary>
    Type,

    /// <summary>SHA-256 do conteúdo diferente do declarado.</summary>
    Hash,

    /// <summary>A imagem já foi recusada antes; a recusa é final e exige declarar um envio novo.</summary>
    AlreadyFailed
}

/// <summary>
/// Lançada quando a <b>confirmação</b> de um envio descobre que o binário no storage não confere com
/// o que foi declarado (FR-006, FR-007). O controller a converte em HTTP 400 com a <see cref="Exception.Message"/>
/// pronta, que já é a frase do contrato para o motivo.
/// <para>
/// <see cref="Reason"/> existe para o serviço decidir o efeito colateral: <see cref="ImageRejectionReason.Size"/>,
/// <see cref="ImageRejectionReason.Type"/> e <see cref="ImageRejectionReason.Hash"/> apagam o objeto e
/// marcam a imagem como <c>failed</c>; <see cref="ImageRejectionReason.NotUploaded"/> e
/// <see cref="ImageRejectionReason.AlreadyFailed"/> não mudam nada (data-model, transições de status).
/// </para>
/// </summary>
public class ImageRejectedException : Exception
{
    /// <summary>Objeto ainda não enviado — o envio continua pendente.</summary>
    public const string NOT_UPLOADED_MESSAGE = "O arquivo da imagem ainda não foi enviado.";

    /// <summary>Tamanho real divergente do declarado ou acima do limite.</summary>
    public const string SIZE_MESSAGE = "O tamanho do arquivo enviado não confere com o declarado ou excede o limite.";

    /// <summary>Conteúdo que não é imagem de tipo aceito.</summary>
    public const string TYPE_MESSAGE = "O conteúdo do arquivo não é uma imagem de tipo aceito.";

    /// <summary>Hash do conteúdo divergente do declarado.</summary>
    public const string HASH_MESSAGE = "O conteúdo do arquivo não confere com o hash declarado.";

    /// <summary>Imagem já recusada em uma confirmação anterior.</summary>
    public const string ALREADY_FAILED_MESSAGE = "Este envio foi recusado. Declare um novo envio.";

    public ImageRejectedException(ImageRejectionReason reason)
        : base(MessageFor(reason))
    {
        Reason = reason;
    }

    /// <summary>Motivo da recusa, usado apenas internamente (não sai no corpo da resposta).</summary>
    public ImageRejectionReason Reason { get; }

    /// <summary>A frase do contrato correspondente ao motivo — o controller devolve <c>ex.Message</c>.</summary>
    private static string MessageFor(ImageRejectionReason reason) => reason switch
    {
        ImageRejectionReason.NotUploaded => NOT_UPLOADED_MESSAGE,
        ImageRejectionReason.Size => SIZE_MESSAGE,
        ImageRejectionReason.Type => TYPE_MESSAGE,
        ImageRejectionReason.Hash => HASH_MESSAGE,
        ImageRejectionReason.AlreadyFailed => ALREADY_FAILED_MESSAGE,
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Motivo de recusa desconhecido.")
    };
}
