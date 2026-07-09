using Microsoft.EntityFrameworkCore;
using PromptOps.Application.Evaluations;
using PromptOps.Domain.Evaluations;
using PromptOps.Infrastructure.Persistence.Mapping;

namespace PromptOps.Infrastructure.Persistence;

/// <summary>
/// Simpler than <see cref="ExecutionRepository"/> — no identity map needed since
/// <see cref="HumanEvaluation"/> is immutable and append-only, the same shape as
/// <see cref="EngineeringMetricsRepository"/>.
/// </summary>
public sealed class HumanEvaluationRepository(PromptOpsDbContext db) : IHumanEvaluationRepository
{
    public async Task AddAsync(HumanEvaluation evaluation, CancellationToken cancellationToken = default)
    {
        await db.HumanEvaluations.AddAsync(HumanEvaluationMapper.ToNewEntity(evaluation), cancellationToken);
    }

    public async Task<IReadOnlyList<HumanEvaluation>> GetByExecutionIdAsync(Guid executionId, CancellationToken cancellationToken = default)
    {
        // SQLite can't ORDER BY DateTimeOffset in SQL (see ExecutionRepository.LoadFullEntityAsync) — sort client-side after load.
        var entities = await db.HumanEvaluations
            .Where(e => e.ExecutionId == executionId)
            .ToListAsync(cancellationToken);

        return entities.OrderBy(e => e.Timestamp).Select(HumanEvaluationMapper.ToDomain).ToList();
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => db.SaveChangesAsync(cancellationToken);
}
