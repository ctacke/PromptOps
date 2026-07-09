using Microsoft.EntityFrameworkCore;
using PromptOps.Application.Scoring;
using PromptOps.Domain.Scoring;
using PromptOps.Infrastructure.Persistence.Mapping;

namespace PromptOps.Infrastructure.Persistence;

/// <summary>Simpler than <see cref="ExecutionRepository"/> — no identity map needed since <see cref="PromptScore"/> is immutable and append-only, the same shape as the Phase 5-7 repositories.</summary>
public sealed class PromptScoreRepository(PromptOpsDbContext db) : IPromptScoreRepository
{
    public async Task AddAsync(PromptScore score, CancellationToken cancellationToken = default)
    {
        await db.PromptScores.AddAsync(PromptScoreMapper.ToNewEntity(score), cancellationToken);
    }

    public async Task<IReadOnlyList<PromptScore>> GetByPromptVersionIdAsync(Guid promptVersionId, CancellationToken cancellationToken = default)
    {
        // SQLite can't ORDER BY DateTimeOffset in SQL (see ExecutionRepository.LoadFullEntityAsync) — sort client-side after load.
        var entities = await db.PromptScores
            .Where(s => s.PromptVersionId == promptVersionId)
            .ToListAsync(cancellationToken);

        return entities.OrderBy(s => s.ComputedAt).Select(PromptScoreMapper.ToDomain).ToList();
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => db.SaveChangesAsync(cancellationToken);
}
