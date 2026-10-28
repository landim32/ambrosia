using Ambrosio.Domain.Models;
using Ambrosio.Infra.Context;
using Ambrosio.Infra.Interfaces.Repository;
using Microsoft.EntityFrameworkCore;

namespace Ambrosio.Infra.Repository;

public class McpAuditRepository : IMcpAuditRepository<McpAuditEntry>
{
    private readonly AmbrosioContext _context;

    public McpAuditRepository(AmbrosioContext context)
    {
        _context = context;
    }

    public async Task<McpAuditEntry> InsertAsync(McpAuditEntry entity)
    {
        _context.McpAuditEntries.Add(entity);
        await _context.SaveChangesAsync();

        return entity;
    }

    public async Task<IReadOnlyList<McpAuditEntry>> ListRecentAsync(long userId, string? toolName, int limit)
    {
        var query = _context.McpAuditEntries
            .AsNoTracking()
            .Where(e => e.UserId == userId);

        if (!string.IsNullOrWhiteSpace(toolName))
        {
            query = query.Where(e => e.ToolName == toolName);
        }

        return await query
            .OrderByDescending(e => e.CreatedAt)
            .ThenByDescending(e => e.Id)
            .Take(limit)
            .ToListAsync();
    }
}
