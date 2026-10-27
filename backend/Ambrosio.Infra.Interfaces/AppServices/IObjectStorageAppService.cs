using Ambrosio.DTO.Images;

namespace Ambrosio.Infra.Interfaces.AppServices;

/// <summary>
/// Storage de objetos compatível com S3 (research §1 a §5, §10). Única abstração que o Domain conhece
/// do provedor: trocar MinIO por Spaces por configuração não toca nenhuma regra de negócio (FR-002).
/// <para>
/// Contrato de erros: qualquer falha de rede ou recusa de serviço (<c>AmazonServiceException</c>,
/// <c>HttpRequestException</c>, timeout) é relançada como <c>StorageUnavailableException</c> com a
/// original apenas em <c>InnerException</c> — o <c>Message</c> público nunca contém endpoint, bucket
/// ou chave, porque os erros 500 devolvem <c>ex.Message</c> ao cliente (FR-021).
/// </para>
/// <para>
/// Nenhuma operação define ACL pública: o bucket é privado e o acesso do navegador acontece só por
/// URL pré-assinada (FR-004).
/// </para>
/// </summary>
public interface IObjectStorageAppService
{
    /// <summary>Grava o binário na chave, com o <c>Content-Type</c> dado (caminho do servidor).</summary>
    Task PutAsync(string key, Stream content, string contentType, CancellationToken ct = default);

    /// <summary>
    /// Cabeçalho do objeto, <b>antes</b> de baixar qualquer byte (research §6).
    /// </summary>
    /// <returns><c>null</c> quando o objeto não existe (404); nunca lança nesse caso.</returns>
    Task<ObjectHead?> HeadAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Abre o binário para leitura em streaming. O chamador é dono do stream e deve limitá-lo a
    /// <c>MaxUploadBytes</c> — um objeto declarado pequeno pode ter sido substituído por um gigante.
    /// </summary>
    Task<Stream> OpenReadAsync(string key, CancellationToken ct = default);

    /// <summary>Remove o objeto. Objeto inexistente <b>não</b> é erro (idempotente, FR-016).</summary>
    Task DeleteAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Remove todos os objetos sob o prefixo (FR-017), com listagem paginada e exclusão em lotes de
    /// até 1000. Usado só pelo fluxo interno de exclusão de conta.
    /// </summary>
    Task DeletePrefixAsync(string prefix, CancellationToken ct = default);

    /// <summary>
    /// URL pré-assinada de <b>leitura</b> (GET), válida por <c>PresignedReadMinutes</c>, gerada a cada
    /// resposta e nunca persistida (FR-012, FR-013, research §5).
    /// </summary>
    /// <param name="key">Chave do objeto.</param>
    /// <param name="now">Instante UTC de referência para a validade (<c>IClock</c>).</param>
    PresignedUrl GetPresignedReadUrl(string key, DateTime now);

    /// <summary>
    /// URL pré-assinada de <b>envio</b> (PUT) com o <c>Content-Type</c> assinado, válida por
    /// <c>PresignedUploadMinutes</c> (research §4). Um <c>PUT</c> com outro tipo recebe 403 do storage.
    /// </summary>
    /// <param name="key">Chave do objeto.</param>
    /// <param name="contentType">Tipo declarado, que o cliente é obrigado a repetir no cabeçalho.</param>
    /// <param name="now">Instante UTC de referência para a validade (<c>IClock</c>).</param>
    PresignedUrl GetPresignedUploadUrl(string key, string contentType, DateTime now);
}
