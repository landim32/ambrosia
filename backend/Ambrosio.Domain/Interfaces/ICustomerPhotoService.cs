using Ambrosio.DTO.CustomerPhotos;
using Ambrosio.DTO.Customers;
using Ambrosio.DTO.Enums;

namespace Ambrosio.Domain.Interfaces;

/// <summary>
/// Regras do álbum de fotos da cliente (feature 009): incluir, listar, descrever, favoritar, excluir,
/// definir e remover a foto de perfil.
/// <para>
/// <b>Nada aqui decodifica imagem.</b> Os bytes entram pelas rotas da feature 004
/// (<c>POST /images</c> na tela, <c>StoreFromServerAsync</c> na conversa) e este serviço só relaciona
/// <c>imageId</c> ↔ cliente, decide o que pode ser apagado do storage e assina as URLs de leitura
/// (research R2/R7). É o que mantém as três histórias de canal — tela, API e ChatGPT — no mesmo corpo
/// de regras (FR-023).
/// </para>
/// <para>
/// Todo método recebe <c>userId</c> extraído do claim <c>sub</c> pelo controller: nenhuma entrada
/// transporta o dono, e recurso de outra vendedora responde <c>NotFoundException</c> com a mesma
/// mensagem de "não existe" (FR-025, SC-006).
/// </para>
/// </summary>
public interface ICustomerPhotoService
{
    /// <summary>
    /// Grade por cursor, mais recente primeiro (FR-010, research R8), com as URLs do
    /// <b>arquivo original</b> — não existe miniatura gerada no servidor (research R3).
    /// </summary>
    Task<CustomerPhotoPage> ListAsync(long userId, long customerId, CustomerPhotoListQuery query);

    /// <summary>
    /// Anexa ao álbum uma imagem já gravada e <c>available</c> do usuário (FR-001, FR-006).
    /// Repetição na mesma cliente não cria segunda foto: devolve a existente com <c>created = false</c>.
    /// </summary>
    /// <param name="canal">
    /// Origem real quando quem chama não é um corpo de requisição — a conversa passa
    /// <see cref="CustomerPhotoSource.ChatGpt"/> aqui. Tem precedência sobre <c>info.Source</c>, que o
    /// validator mantém fechado a <c>web</c>/<c>api</c> para que uma integração não se passe pela
    /// conversa declarando a origem no JSON (research R12). Sem canal explícito, vale o corpo.
    /// </param>
    Task<CustomerPhotoInsertResult> AttachAsync(
        long userId, long customerId, CustomerPhotoInsertInfo info, CustomerPhotoSource? canal = null);

    /// <summary>Cria, troca ou apaga a descrição da foto (FR-007). <c>null</c> significa sem descrição.</summary>
    Task<CustomerPhotoInfo> UpdateDescriptionAsync(
        long userId, long customerId, long photoId, CustomerPhotoDescriptionInfo info);

    /// <summary>Liga ou desliga o coração, idempotente (FR-008, SC-008).</summary>
    Task<CustomerPhotoInfo> SetFavoriteAsync(long userId, long customerId, long photoId, bool favorite);

    /// <summary>
    /// Remove a foto do álbum e, se nenhum outro uso restar, o arquivo do storage (FR-009). Quando a
    /// foto é a origem do perfil, o perfil é removido junto (FR-015).
    /// </summary>
    Task DeleteAsync(long userId, long customerId, long photoId);

    /// <summary>
    /// Define a foto de perfil (FR-011/FR-012). <c>ProfilePhotoUpdateInfo.ImageId</c> é o recorte
    /// quadrado já gravado, produzido <b>no navegador</b>; nulo faz o círculo usar o arquivo da
    /// própria foto, que é o caso da conversa sem navegador (FR-013).
    /// </summary>
    Task<CustomerProfilePhotoInfo> SetProfileAsync(long userId, long customerId, ProfilePhotoUpdateInfo info);

    /// <summary>Remove o perfil sem tocar no álbum (FR-015).</summary>
    Task RemoveProfileAsync(long userId, long customerId);

    /// <summary>Total de fotos da cliente (contador do perfil e <c>totalFotos</c> da conversa).</summary>
    Task<int> CountAsync(long userId, long customerId);

    /// <summary>
    /// Avatares de muitas clientes numa só consulta, para <see cref="CustomerInfo.ProfilePhoto"/>
    /// sem N+1 na listagem (T042). Clientes sem perfil ficam de fora do dicionário.
    /// </summary>
    /// <param name="includeOriginalUrl">
    /// <c>false</c> na listagem: assinar a URL do original custa uma assinatura por linha e só o
    /// detalhe precisa dela para abrir a foto inteira (data-model §6).
    /// </param>
    Task<Dictionary<long, CustomerProfilePhotoInfo>> GetProfilesAsync(
        long userId, IReadOnlyCollection<long> customerIds, bool includeOriginalUrl);

    /// <summary>
    /// Remove o álbum inteiro da cliente (research R13). Sem rota nesta entrega — não existe
    /// <c>DELETE /customers/{id}</c> —, chamado pela futura exclusão de cliente e pela de conta.
    /// </summary>
    /// <returns>Quantidade de fotos removidas.</returns>
    Task<int> DeleteAllForCustomerAsync(long userId, long customerId);
}
