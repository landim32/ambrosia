using Ambrosio.Domain.Exceptions;
using Ambrosio.Domain.Models;
using Ambrosio.DTO.CustomerPhotos;
using Ambrosio.Infra.Context;
using Ambrosio.Infra.Interfaces.Repository;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ambrosio.Infra.Repository;

/// <summary>
/// Acesso à tabela <c>customer_photos</c> com o isolamento estrutural da casa (FR-025):
/// <c>Where(p => p.UserId == userId)</c> é sempre a <b>primeira</b> cláusula de toda leitura, então
/// foto de outra vendedora é indistinguível de foto inexistente — o 404 único do contrato.
/// <para>
/// A ordenação da grade (<see cref="ListAsync"/>) é feita com o par
/// <c>(created_at, customer_photo_id)</c> <b>descendo juntos</b>: o id desempata duas fotos gravadas
/// no mesmo instante, que é exatamente o caso do arraste em lote, e sem desempate o cursor
/// repetiria ou pularia fotos (research R8).
/// </para>
/// </summary>
public class CustomerPhotoRepository : ICustomerPhotoRepository<CustomerPhoto>
{
    /// <summary>SQLSTATE de violação de unicidade no PostgreSQL.</summary>
    private const string UniqueViolation = "23505";

    private readonly AmbrosioContext _context;

    public CustomerPhotoRepository(AmbrosioContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task<CustomerPhoto?> GetByIdAsync(long userId, long customerId, long photoId)
    {
        return await _context.CustomerPhotos
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .Where(p => p.CustomerId == customerId)
            .Where(p => p.Id == photoId)
            .Include(p => p.StoredImage)
            .FirstOrDefaultAsync();
    }

    /// <inheritdoc />
    public async Task<CustomerPhoto?> FindByImageAsync(long userId, long customerId, long imageId)
    {
        return await _context.CustomerPhotos
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .Where(p => p.CustomerId == customerId)
            .Where(p => p.ImageId == imageId)
            .FirstOrDefaultAsync();
    }

    /// <inheritdoc />
    public async Task<List<CustomerPhoto>> ListAsync(
        long userId, long customerId, long? cursor, int take, bool favorites)
    {
        var query = _context.CustomerPhotos
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .Where(p => p.CustomerId == customerId)
            .Include(p => p.StoredImage)
            .AsQueryable();

        if (favorites)
        {
            query = query.Where(p => p.IsFavorite);
        }

        // Keyset: "estritamente mais antigas que a última que mostrei", na ordem DESC. A fronteira é
        // lida antes porque Npgsql não traduz comparação contra um tuplo literal, e porque o cursor
        // pode apontar para uma foto apagada no meio da rolagem — nesse caso a grade volta à primeira
        // página em vez de mostrar lista vazia (quem rola não sabe que a foto saiu).
        if (cursor is > 0)
        {
            var cursorCreatedAt = await _context.CustomerPhotos
                .AsNoTracking()
                .Where(p => p.UserId == userId)
                .Where(p => p.Id == cursor.Value)
                .Select(p => p.CreatedAt)
                .FirstOrDefaultAsync();

            if (cursorCreatedAt != default)
            {
                var cursorId = cursor.Value;

                query = query.Where(p => p.CreatedAt < cursorCreatedAt
                                         || (p.CreatedAt == cursorCreatedAt && p.Id < cursorId));
            }
        }

        return await query
            .OrderByDescending(p => p.CreatedAt)
            .ThenByDescending(p => p.Id)
            .Take(take)
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task<int> CountForCustomerAsync(long userId, long customerId)
    {
        return await _context.CustomerPhotos
            .Where(p => p.UserId == userId)
            .Where(p => p.CustomerId == customerId)
            .CountAsync();
    }

    /// <inheritdoc />
    public async Task<bool> ExistsInOtherCustomerAsync(long userId, long customerId, long imageId)
    {
        return await _context.CustomerPhotos
            .Where(p => p.UserId == userId)
            .Where(p => p.ImageId == imageId)
            .Where(p => p.CustomerId != customerId)
            .AnyAsync();
    }

    /// <inheritdoc />
    public async Task<List<long>> ListCustomersUsingAsync(long userId, long imageId)
    {
        return await _context.CustomerPhotos
            .Where(p => p.UserId == userId)
            .Where(p => p.ImageId == imageId)
            .Select(p => p.CustomerId)
            .Distinct()
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task<CustomerPhoto> InsertAsync(CustomerPhoto entity)
    {
        _context.CustomerPhotos.Add(entity);

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Duas requisições paralelas anexando a mesma imagem à mesma cliente: o índice único
            // decide o vencedor e o perdedor é relido pelo serviço (research R6, FR-006).
            throw new DuplicateException("Esta foto já está no álbum da cliente.");
        }

        return entity;
    }

    /// <inheritdoc />
    public async Task<CustomerPhoto> UpdateAsync(CustomerPhoto entity)
    {
        // Mesmo padrão de StoredImageRepository: procura rastreado pelo par (id, user_id) e aplica os
        // valores — assim uma chamada errada não consegue escrever foto de outra conta.
        var existing = await _context.CustomerPhotos
            .FirstOrDefaultAsync(p => p.Id == entity.Id && p.UserId == entity.UserId);

        if (existing is null)
        {
            throw new NotFoundException("Foto não encontrada.");
        }

        _context.Entry(existing).CurrentValues.SetValues(entity);
        await _context.SaveChangesAsync();

        return existing;
    }

    /// <inheritdoc />
    public async Task<long?> DeleteWithProfileClearAsync(long userId, long photoId, DateTime now)
    {
        // Sem AsNoTracking: as duas linhas mudam juntas no mesmo SaveChanges.
        var photo = await _context.CustomerPhotos
            .Where(p => p.UserId == userId)
            .FirstOrDefaultAsync(p => p.Id == photoId);

        if (photo is null)
        {
            // Já apagada (a vendedora clicou duas vezes): apagar o que não existe não apaga nada,
            // pelo mesmo motivo que os DeleteAsync de imagem usam ExecuteDelete.
            return null;
        }

        long? orphanedProfileImageId = null;

        var customer = await _context.Customers
            .Where(c => c.UserId == userId)
            .Where(c => c.ProfilePhotoId == photoId)
            .FirstOrDefaultAsync();

        if (customer is not null)
        {
            // Comportamento do modelo, não SQL à mão (Princípio V): ele sabe que limpar o perfil é
            // um estado, e devolve o recorte que ficou sem dono.
            orphanedProfileImageId = customer.ClearProfilePhoto(now);
        }

        _context.CustomerPhotos.Remove(photo);

        // Um único SaveChanges = uma transação pelo EF: nunca fica perfil apontando para foto
        // apagada, nem foto apagada com o perfil ainda no lugar (FR-015).
        await _context.SaveChangesAsync();

        return orphanedProfileImageId;
    }

    /// <inheritdoc />
    public async Task<List<CustomerProfileProjection>> ListProfilesAsync(
        long userId, IReadOnlyCollection<long> customerIds)
    {
        if (customerIds.Count == 0)
        {
            return [];
        }

        // Uma ida ao banco para a página inteira (T042). O `ProfilePhoto!.ImageId` vira JOIN interno;
        // a projeção é montada em C# para não depender de EF traduzir `.Value` de um nullable.
        var rows = await _context.Customers
            .AsNoTracking()
            .Where(c => c.UserId == userId)
            .Where(c => customerIds.Contains(c.Id))
            .Where(c => c.ProfilePhotoId != null)
            .Select(c => new
            {
                c.Id,
                PhotoId = c.ProfilePhotoId,
                CropImageId = c.ProfileImageId,
                AlbumImageId = c.ProfilePhoto!.ImageId
            })
            .ToListAsync();

        return rows
            .Select(r => new CustomerProfileProjection(
                r.Id,
                r.PhotoId ?? 0,
                r.CropImageId,
                r.AlbumImageId))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<List<long>> ListImageIdsForCustomerAsync(long userId, long customerId)
    {
        return await _context.CustomerPhotos
            .Where(p => p.UserId == userId)
            .Where(p => p.CustomerId == customerId)
            .Select(p => p.ImageId)
            .Distinct()
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task<List<long>> ListImageIdsForUserAsync(long userId)
    {
        return await _context.CustomerPhotos
            .Where(p => p.UserId == userId)
            .Select(p => p.ImageId)
            .Distinct()
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task DeleteAllForCustomerAsync(long userId, long customerId)
    {
        await _context.CustomerPhotos
            .Where(p => p.UserId == userId)
            .Where(p => p.CustomerId == customerId)
            .ExecuteDeleteAsync();
    }

    /// <inheritdoc />
    public async Task DeleteAllForUserAsync(long userId)
    {
        await _context.CustomerPhotos
            .Where(p => p.UserId == userId)
            .ExecuteDeleteAsync();
    }

    /// <inheritdoc />
    public async Task ClearProfilesForUserAsync(long userId, DateTime now)
    {
        // ExecuteDeleteUpdate em statement único: a exclusão de conta já apaga as clientes em
        // seguida, então não há porque materializar linha por linha.
        await _context.Customers
            .Where(c => c.UserId == userId)
            .Where(c => c.ProfilePhotoId != null || c.ProfileImageId != null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(c => c.ProfilePhotoId, (long?)null)
                .SetProperty(c => c.ProfileImageId, (long?)null)
                .SetProperty(c => c.UpdatedAt, now));
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: UniqueViolation };
}
