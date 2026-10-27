namespace Ambrosio.DTO.Enums;

/// <summary>
/// Origem do binário de uma imagem (data-model). Persistido como <c>integer</c>; no JSON:
/// <c>upload</c> | <c>server</c> | <c>chatGpt</c> | <c>bot</c>. Em todas as origens os bytes são
/// gravados exatamente como chegaram.
/// </summary>
public enum ImageSource
{
    /// <summary>Enviado direto pelo cliente ao storage via URL pré-assinada (HU 001).</summary>
    Upload = 1,

    /// <summary>Gravado pelo próprio servidor no fluxo multipart (FR-008).</summary>
    Server = 2,

    /// <summary>Reservado às histórias consumidoras: imagem gerada pela conversa (HU 010).</summary>
    ChatGpt = 3,

    /// <summary>Reservado às histórias consumidoras: imagem enviada pelo bot (HU 004).</summary>
    Bot = 4
}
