namespace Ambrosia.DTO.Enums;

/// <summary>
/// Origem do binário de uma imagem (data-model). Persistido como <c>integer</c>; no JSON:
/// <c>upload</c> | <c>server</c> | <c>chatGpt</c> | <c>bot</c>. Define se a sanitização de metadados
/// foi aplicada: <see cref="Upload"/> preserva os bytes exatamente como o cliente os enviou
/// (clarificação 2 da spec), os demais passam pelo processador do servidor.
/// </summary>
public enum ImageSource
{
    /// <summary>Enviado direto pelo cliente ao storage via URL pré-assinada (HU 001).</summary>
    Upload = 1,

    /// <summary>Gravado pelo próprio servidor no fluxo multipart, sanitizado (FR-010).</summary>
    Server = 2,

    /// <summary>Reservado às histórias consumidoras: imagem gerada pela conversa (HU 010).</summary>
    ChatGpt = 3,

    /// <summary>Reservado às histórias consumidoras: imagem enviada pelo bot (HU 004).</summary>
    Bot = 4
}
