using Ambrosio.Domain.Exceptions;
using Ambrosio.Domain.Models;
using Ambrosio.DTO.Enums;
using Ambrosio.Infra.Context;
using Ambrosio.Infra.Interfaces.Repository;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ambrosio.Infra.Repository;

/// <summary>
/// Acesso à tabela <c>stored_images</c> com o mesmo isolamento estrutural dos demais repositórios
/// (FR-014): <c>Where(i => i.UserId == userId)</c> é sempre a <b>primeira</b> cláusula de toda
/// leitura por usuário, então imagem de outra conta é indistinguível de imagem inexistente — o 404
/// único que o contrato pede.
/// <para>
/// As duas exclusões usam <c>ExecuteDelete</c> (statement único, sem passar pelo change tracker): o
/// dono pode excluir a imagem no mesmo instante em que o job de limpeza a varre, e um
/// <c>Remove</c> rastreado transformaria essa corrida em <c>DbUpdateConcurrencyException</c> — um 500
/// para apagar algo que já não existe. Assim, apagar o que já foi apagado simplesmente não apaga nada.
/// </para>
/// </summary>
public class StoredImageRepository : IStoredImageRepository<StoredImage>
{
    /// <summary>SQLSTATE de violação de unicidade no PostgreSQL (research §8).</summary>
    private const string UniqueViolation = "23505";

    private readonly AmbrosioContext _context;

    public StoredImageRepository(AmbrosioContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task<StoredImage?> GetByIdAsync(long imageId, long userId)
    {
        return await _context.StoredImages
            .AsNoTracking()
            .Where(i => i.UserId == userId)
            .FirstOrDefaultAsync(i => i.Id == imageId);
    }

    /// <inheritdoc />
    public async Task<List<StoredImage>> GetByIdsAsync(long userId, IReadOnlyCollection<long> imageIds)
    {
        if (imageIds.Count == 0)
        {
            return [];
        }

        // `user_id` primeiro, como em toda leitura da casa (§VI): imagem de outra conta não aparece,
        // e quem chama trata a ausência como "não existe".
        return await _context.StoredImages
            .AsNoTracking()
            .Where(i => i.UserId == userId)
            .Where(i => imageIds.Contains(i.Id))
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task<StoredImage?> GetActiveByHashAsync(long userId, string sha256)
    {
        // Status 3 (failed) fica fora do filtro: é o que permite redeclarar o mesmo conteúdo depois
        // de uma recusa (clarificação 5), e casa com o índice único parcial ux_stored_images_user_sha256_active.
        return await _context.StoredImages
            .AsNoTracking()
            .Where(i => i.UserId == userId)
            .Where(i => i.Sha256 == sha256)
            .Where(i => i.Status == ImageStatus.Pending || i.Status == ImageStatus.Available)
            .FirstOrDefaultAsync();
    }

    /// <inheritdoc />
    public async Task<StoredImage> InsertAsync(StoredImage entity)
    {
        _context.StoredImages.Add(entity);

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Dois pedidos simultâneos do mesmo conteúdo: o índice único parcial decide o vencedor e
            // o perdedor é relido pelo serviço, que reaproveita a imagem (research §8).
            throw new DuplicateException("Já existe uma imagem com este conteúdo.");
        }

        return entity;
    }

    /// <inheritdoc />
    public async Task<StoredImage> UpdateAsync(StoredImage entity)
    {
        var existing = await _context.StoredImages
            .FirstOrDefaultAsync(i => i.Id == entity.Id && i.UserId == entity.UserId);

        if (existing is null)
        {
            throw new NotFoundException("Imagem não encontrada.");
        }

        _context.Entry(existing).CurrentValues.SetValues(entity);
        await _context.SaveChangesAsync();

        return existing;
    }

    /// <inheritdoc />
    public async Task DeleteAsync(StoredImage entity)
    {
        // Filtro pelo dono também aqui: a chave primária sozinha bastaria, mas manter o par
        // (id, user_id) deixa impossível que uma chamada errada apague linha de outra conta.
        await _context.StoredImages
            .Where(i => i.Id == entity.Id && i.UserId == entity.UserId)
            .ExecuteDeleteAsync();
    }

    /// <inheritdoc />
    public async Task<List<StoredImage>> ListStaleAsync(DateTime pendingBefore, DateTime failedBefore, int take)
    {
        // Única leitura SEM filtro de usuário (exceção §VI justificada no plano): atende só o job de
        // limpeza (FR-018) e jamais um endpoint. Pendente se mede desde a criação (o envio pode nunca
        // ter chegado); falha se mede desde a última transição de status.
        return await _context.StoredImages
            .AsNoTracking()
            .Where(i => (i.Status == ImageStatus.Pending && i.CreatedAt < pendingBefore)
                        || (i.Status == ImageStatus.Failed && i.UpdatedAt < failedBefore))
            .OrderBy(i => i.CreatedAt)
            .Take(take)
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task DeleteAllByUserAsync(long userId)
    {
        await _context.StoredImages
            .Where(i => i.UserId == userId)
            .ExecuteDeleteAsync();
    }

    /// <inheritdoc />
    public async Task<bool> IsImageReferencedAsync(long imageId, long userId)
    {
        // Três leitores possíveis, todos filtrados pelo dono como primeira cláusula (§VI): outra foto
        // do álbum (de qualquer cliente da conta), o recorte de perfil de alguma cliente, e item de
        // pedido importado. O último passa pela navegação porque `order_items` não carrega user_id —
        // o dono é do pedido.
        var inAlbum = await _context.CustomerPhotos
            .Where(p => p.UserId == userId)
            .Where(p => p.ImageId == imageId)
            .AnyAsync();

        if (inAlbum)
        {
            return true;
        }

        var inProfile = await _context.Customers
            .Where(c => c.UserId == userId)
            .Where(c => c.ProfileImageId == imageId)
            .AnyAsync();

        if (inProfile)
        {
            return true;
        }

        return await _context.OrderItems
            .Where(i => i.Order!.UserId == userId)
            .Where(i => i.ImageId == imageId)
            .AnyAsync();
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: UniqueViolation };
}
