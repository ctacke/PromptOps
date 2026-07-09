using Microsoft.EntityFrameworkCore;
using PromptOps.Application.Metrics;
using PromptOps.Domain.Metrics;
using PromptOps.Infrastructure.Persistence.Mapping;

namespace PromptOps.Infrastructure.Persistence;

/// <summary>
/// Simpler than <see cref="ExecutionRepository"/> — no identity map needed since
/// <see cref="EngineeringMetrics"/> is immutable and append-only (docs/metrics.md).
/// </summary>
public sealed class EngineeringMetricsRepository(PromptOpsDbContext db) : IEngineeringMetricsRepository
{
    public async Task AddAsync(EngineeringMetrics metrics, CancellationToken cancellationToken = default)
    {
        await db.EngineeringMetrics.AddAsync(EngineeringMetricsMapper.ToNewEntity(metrics), cancellationToken);
    }

    public async Task<IReadOnlyList<EngineeringMetrics>> GetByExecutionIdAsync(Guid executionId, CancellationToken cancellationToken = default)
    {
        // SQLite can't ORDER BY DateTimeOffset in SQL (see ExecutionRepository.LoadFullEntityAsync
        // for the same issue) — sort client-side after load instead.
        var entities = await db.EngineeringMetrics
            .Where(m => m.ExecutionId == executionId)
            .ToListAsync(cancellationToken);

        return entities.OrderBy(m => m.CollectedAt).Select(EngineeringMetricsMapper.ToDomain).ToList();
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => db.SaveChangesAsync(cancellationToken);
}
