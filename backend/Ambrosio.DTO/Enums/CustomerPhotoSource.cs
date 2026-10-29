namespace Ambrosio.DTO.Enums;

/// <summary>
/// Canal por que a foto entrou no álbum de uma cliente (feature 009, FR-002, research R12).
/// Persistido como <c>integer</c>; no JSON: <c>web</c> | <c>api</c> | <c>chatGpt</c>.
/// É campo informativo: não concede nem nega permissão nenhuma.
/// </summary>
public enum CustomerPhotoSource
{
    /// <summary>Tela do Ambrosio (a vendedora arrastou ou escolheu o arquivo).</summary>
    Web = 1,

    /// <summary>Integração pela API REST — padrão quando o corpo não declara a origem.</summary>
    Api = 2,

    /// <summary>Conversa no ChatGPT (MCP). Nunca aceito no corpo da rota REST, para que uma
    /// integração não se passe pelo canal da conversa (research R12).</summary>
    ChatGpt = 3
}
