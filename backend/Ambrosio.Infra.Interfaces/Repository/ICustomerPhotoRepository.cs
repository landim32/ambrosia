using Ambrosio.DTO.CustomerPhotos;

namespace Ambrosio.Infra.Interfaces.Repository;

/// <summary>
/// Persistência do álbum de fotos da cliente (feature 009, tabela <c>customer_photos</c>).
/// <para>
/// Isolamento estrutural (constituição §VI, FR-025): todo método de <b>leitura</b> exige
/// <c>userId</c> e a implementação aplica <c>Where(p => p.UserId == userId)</c> como
/// <b>primeira</b> cláusula — não existe consulta sem filtro para esquecer de usar. Recurso de
/// outra vendedora é <c>null</c>, indistinguível de recurso inexistente (o 404 único do contrato).
/// </para>
/// <para>
/// Interface genérica como as demais repositórios da casa: nada aqui menciona os modelos do
/// Domain, para que <c>Ambrosio.Infra.Interfaces</c> continue sem dependências.
/// </para>
/// </summary>
/// <typeparam name="TModel">Modelo da foto.</typeparam>
public interface ICustomerPhotoRepository<TModel> where TModel : class
{
    /// <summary>Foto pelo id, só se for do usuário e desta cliente; <c>null</c> nos dois casos.</summary>
    Task<TModel?> GetByIdAsync(long userId, long customerId, long photoId);

    /// <summary>Foto desta cliente para esta imagem — é a consulta da deduplicação (FR-006).</summary>
    Task<TModel?> FindByImageAsync(long userId, long customerId, long imageId);

    /// <summary>
    /// Grade por cursor (research R8): mais recente primeiro, desempate por id. Lê no máximo
    /// <paramref name="take"/> linhas — quem chama pede <c>pageSize + 1</c> para saber se há página
    /// seguinte sem contar a tabela.
    /// </summary>
    /// <param name="cursor">Id da última foto da página anterior; <c>null</c> = primeira página.</param>
    /// <param name="favorites"><c>true</c> usa só as favoritas (FR-010).</param>
    Task<List<TModel>> ListAsync(long userId, long customerId, long? cursor, int take, bool favorites);

    /// <summary>Total de fotos da cliente (contador do perfil e <c>total</c> da conversa).</summary>
    Task<int> CountForCustomerAsync(long userId, long customerId);

    /// <summary>
    /// A mesma imagem já está no álbum de <b>outra</b> cliente desta vendedora (edge case "mesma foto
    /// em duas clientes"): só informativo, porque a linha é por cliente.
    /// </summary>
    Task<bool> ExistsInOtherCustomerAsync(long userId, long customerId, long imageId);

    /// <summary>
    /// Ids das clientes desta vendedora que já têm esta imagem no álbum. Usado pela conversa para
    /// avisar onde mais a foto apareceu (contracts/mcp-tools.md §2).
    /// </summary>
    Task<List<long>> ListCustomersUsingAsync(long userId, long imageId);

    /// <summary>Insere; violação de <c>ux_customer_photos_customer_image</c> vira <c>DuplicateException</c>.</summary>
    Task<TModel> InsertAsync(TModel entity);

    /// <summary>Grava a foto já carregada (descrição e favorita — FR-007/FR-008).</summary>
    Task<TModel> UpdateAsync(TModel entity);

    /// <summary>
    /// Remove a foto e, <b>no mesmo <c>SaveChanges</c></b>, limpa o perfil da cliente quando ele
    /// apontava para ela (FR-015 e edge case "foto de perfil excluída"). Um único
    /// <c>SaveChanges</c> é atômico pelo próprio EF (Princípio V: nada de cascata no banco).
    /// </summary>
    /// <returns>Id do recorte de perfil que ficou sem dono, se havia (<c>null</c> caso contrário) — o
    /// serviço usa isso na checagem de órfão (research R6).</returns>
    Task<long?> DeleteWithProfileClearAsync(long userId, long photoId, DateTime now);

    /// <summary>
    /// Fotos de perfil das clientes dadas (feature 009, T042), já com a imagem da foto de origem.
    /// Uma consulta para muitas clientes, porque <c>CustomerInfo</c> precisa do avatar em toda linha
    /// da listagem — resolver uma a cada linha seria um N+1.
    /// </summary>
    Task<List<CustomerProfileProjection>> ListProfilesAsync(
        long userId, IReadOnlyCollection<long> customerIds);

    /// <summary>
    /// Ids das imagens do álbum da cliente (research R13). Separado de <c>DeleteAllForCustomerAsync</c>
    /// porque a exclusão precisa saber <b>antes</b> quais imagens verificar como órfãs, e carregar as
    /// entidades inteiras de um álbum grande para ler um campo não se paga.
    /// </summary>
    Task<List<long>> ListImageIdsForCustomerAsync(long userId, long customerId);

    /// <summary>Ids de todas as imagens do álbum da conta (exclusão de conta, research R13).</summary>
    Task<List<long>> ListImageIdsForUserAsync(long userId);

    /// <summary>Remove todas as fotos da cliente (research R13 — futura exclusão de cliente).</summary>
    Task DeleteAllForCustomerAsync(long userId, long customerId);

    /// <summary>
    /// Remove as fotos da conta inteira (research R13, fluxo de exclusão de conta). Sem checagem de
    /// órfão aqui: quem chama apaga as <c>stored_images</c> da conta logo em seguida.
    /// </summary>
    Task DeleteAllForUserAsync(long userId);

    /// <summary>
    /// Zera as colunas de perfil de todas as clientes da conta, antes da remoção das imagens
    /// (as FKs novas não têm cascata).
    /// </summary>
    Task ClearProfilesForUserAsync(long userId, DateTime now);
}
