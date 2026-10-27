namespace Ambrosio.Infra.Interfaces.Repository;

/// <summary>
/// Persistência das imagens (feature 004). Espelha o isolamento estrutural dos demais repositórios
/// (FR-014): todo método de <b>leitura</b> recebe <c>userId</c> e a implementação aplica
/// <c>Where(i => i.UserId == userId)</c> como <b>primeira</b> cláusula, de modo que imagem de outro
/// usuário é indistinguível de imagem inexistente (o 404 do contrato).
/// <para>
/// <b>Exceção justificada à constituição §VI</b> (Complexity Tracking do plano): <see cref="ListStaleAsync"/>
/// lê sem <c>userId</c>, porque o job de limpeza varre pendentes e falhas de <b>todos</b> os usuários.
/// Ela não atende requisição nenhuma: é chamada apenas pelo <c>PendingImageCleanupService</c> e nunca
/// por um endpoint — conferir isso na revisão de segurança (T067).
/// </para>
/// </summary>
/// <typeparam name="TModel">Modelo da imagem armazenada.</typeparam>
public interface IStoredImageRepository<TModel> where TModel : class
{
    /// <summary>Imagem pelo id, só se for do usuário; <c>null</c> nos dois casos (FR-014).</summary>
    Task<TModel?> GetByIdAsync(long imageId, long userId);

    /// <summary>
    /// Imagem <c>pending</c> ou <c>available</c> com este hash para o usuário (FR-011). <c>failed</c>
    /// fica de fora de propósito: uma recusa anterior não bloqueia um reenvio do mesmo conteúdo
    /// (clarificação 5 da spec).
    /// </summary>
    Task<TModel?> GetActiveByHashAsync(long userId, string sha256);

    /// <summary>Insere; violação do índice único parcial vira <c>DuplicateException</c> (research §8).</summary>
    Task<TModel> InsertAsync(TModel entity);

    Task<TModel> UpdateAsync(TModel entity);

    Task DeleteAsync(TModel entity);

    /// <summary>
    /// Varredura do job de limpeza (FR-018): pendentes criadas antes de <paramref name="pendingBefore"/>
    /// e falhas atualizadas antes de <paramref name="failedBefore"/>, no máximo <paramref name="take"/>
    /// por chamada.
    /// </summary>
    /// <remarks>
    /// Única leitura sem filtro de usuário da camada, e <b>uso restrito ao job de limpeza</b>: nenhum
    /// endpoint pode chamá-la, sob pena de vazar a existência de imagens de outras contas (exceção §VI).
    /// </remarks>
    Task<List<TModel>> ListStaleAsync(DateTime pendingBefore, DateTime failedBefore, int take);

    /// <summary>Remove os metadados de todas as imagens do usuário (FR-017, fluxo interno).</summary>
    Task DeleteAllByUserAsync(long userId);
}
