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

    public async Task<ScoreStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        var count = await db.PromptScores.AsNoTracking().CountAsync(cancellationToken);
        // AverageAsync throws on an empty sequence — guard rather than let a fresh daemon 500 here.
        double? average = count > 0
            ? await db.PromptScores.AsNoTracking().AverageAsync(s => s.OverallScore, cancellationToken)
            : null;

        return new ScoreStatistics(count, average);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => db.SaveChangesAsync(cancellationToken);
}
