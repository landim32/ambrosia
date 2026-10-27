using Ambrosia.DTO.Enums;
using Ambrosia.DTO.Images;

namespace Ambrosia.Domain.Interfaces;

/// <summary>
/// Regras de vida da imagem (feature 004): declarar envio, confirmar o binário que chegou ao
/// storage, gravar pelo servidor, consultar, excluir e limpar abandonos. As regras ficam aqui; o
/// contato com AWS SDK e ImageSharp fica atrás das abstrações da Infra (plan.md, Project Structure).
/// <para>
/// Todo método recebe <c>userId</c> extraído do claim <c>sub</c> pelo controller: nenhuma entrada
/// transporta o dono, e recurso de outro usuário é <c>NotFoundException</c> — nunca uma resposta que
/// revele que existe (FR-014, SC-008).
/// </para>
/// </summary>
public interface IImageService
{
    /// <summary>
    /// Declaração de envio pelo cliente (FR-003, FR-011): cria o registro <c>pending</c> e devolve a
    /// URL pré-assinada de <c>PUT</c>, ou reaproveita a imagem já disponível do mesmo conteúdo.
    /// </summary>
    /// <returns>Resultado da declaração e <c>true</c> quando há um envio a fazer (201), <c>false</c>
    /// quando o conteúdo já está disponível (200).</returns>
    Task<(ImageUploadResult Result, bool Created)> RequestUploadAsync(long userId, ImageUploadInsertInfo info);

    /// <summary>
    /// Conferência obrigatória do que chegou ao storage: tamanho, tipo real e SHA-256 (FR-006, FR-007).
    /// Aprova para <c>available</c> ou recusa apagando o objeto.
    /// </summary>
    Task<ImageInfo> ConfirmUploadAsync(long userId, long imageId);

    /// <summary>
    /// Gravação pelo próprio servidor (FR-008 a FR-010): valida pelo conteúdo, remove os metadados de
    /// privacidade, re-codifica, calcula o hash do resultado e aplica a deduplicação.
    /// </summary>
    /// <returns>A imagem (nova ou existente) e <c>true</c> quando o registro foi criado agora.</returns>
    Task<(ImageInfo Image, bool Created)> StoreFromServerAsync(long userId, Stream content, ImageSource source);

    /// <summary>Consulta uma imagem do usuário, com URL de leitura nova quando disponível (FR-012, FR-015).</summary>
    Task<ImageInfo> GetByIdAsync(long userId, long imageId);

    /// <summary>Exclusão definitiva pelo dono: objeto no storage e metadado (FR-016).</summary>
    Task DeleteAsync(long userId, long imageId);

    /// <summary>
    /// Remove todas as imagens de um usuário (FR-017). Sem rota: uso interno pelo futuro fluxo de
    /// exclusão de conta.
    /// </summary>
    Task DeleteAllForUserAsync(long userId);

    /// <summary>
    /// Varredura do job de limpeza (FR-018): remove pendentes e falhas mais velhas que
    /// <c>PendingTtlMinutes</c>. Chamada só pelo <c>PendingImageCleanupService</c>.
    /// </summary>
    /// <returns>Total de metadados removidos.</returns>
    Task<int> PurgeStaleAsync(DateTime now);
}
