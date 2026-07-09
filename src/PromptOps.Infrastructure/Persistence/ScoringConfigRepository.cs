using Microsoft.EntityFrameworkCore;
using PromptOps.Application.Scoring;
using PromptOps.Domain.Scoring;
using PromptOps.Infrastructure.Persistence.Mapping;

namespace PromptOps.Infrastructure.Persistence;

public sealed class ScoringConfigRepository(PromptOpsDbContext db) : IScoringConfigRepository
{
    public async Task AddAsync(ScoringConfig config, CancellationToken cancellationToken = default)
    {
        await db.ScoringConfigs.AddAsync(ScoringConfigMapper.ToNewEntity(config), cancellationToken);
    }

    public async Task<ScoringConfig?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await db.ScoringConfigs.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        return entity is null ? null : ScoringConfigMapper.ToDomain(entity);
    }

    public async Task<ScoringConfig?> GetLatestByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        // Highest Version wins — "latest" for a name is defined purely by version number, no
        // separate "active" flag (see ScoringService's docs).
        var entity = await db.ScoringConfigs
            .Where(c => c.Name == name)
            .OrderByDescending(c => c.Version)
            .FirstOrDefaultAsync(cancellationToken);

        return entity is null ? null : ScoringConfigMapper.ToDomain(entity);
    }

    public async Task<IReadOnlyList<ScoringConfig>> GetAllByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        // Unlike the DateTimeOffset fields elsewhere in this project, Version is a plain int —
        // SQLite has no trouble ordering by it in SQL directly.
        var entities = await db.ScoringConfigs
            .Where(c => c.Name == name)
            .OrderBy(c => c.Version)
            .ToListAsync(cancellationToken);

        return entities.Select(ScoringConfigMapper.ToDomain).ToList();
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => db.SaveChangesAsync(cancellationToken);
}
