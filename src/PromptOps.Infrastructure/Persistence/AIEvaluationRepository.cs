using Microsoft.EntityFrameworkCore;
using PromptOps.Application.Evaluations;
using PromptOps.Domain.Evaluations;
using PromptOps.Infrastructure.Persistence.Mapping;

namespace PromptOps.Infrastructure.Persistence;

/// <summary>
/// Simpler than <see cref="ExecutionRepository"/> — no identity map needed since
/// <see cref="AIEvaluation"/> is immutable and append-only, the same shape as
/// <see cref="HumanEvaluationRepository"/>/<see cref="EngineeringMetricsRepository"/>.
/// </summary>
public sealed class AIEvaluationRepository(PromptOpsDbContext db) : IAIEvaluationRepository
{
    public async Task AddAsync(AIEvaluation evaluation, CancellationToken cancellationToken = default)
    {
        await db.AIEvaluations.AddAsync(AIEvaluationMapper.ToNewEntity(evaluation), cancellationToken);
    }

    public async Task<IReadOnlyList<AIEvaluation>> GetByExecutionIdAsync(Guid executionId, CancellationToken cancellationToken = default)
    {
        // SQLite can't ORDER BY DateTimeOffset in SQL (see ExecutionRepository.LoadFullEntityAsync) — sort client-side after load.
        var entities = await db.AIEvaluations
            .Where(e => e.ExecutionId == executionId)
            .ToListAsync(cancellationToken);

        return entities.OrderBy(e => e.Timestamp).Select(AIEvaluationMapper.ToDomain).ToList();
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => db.SaveChangesAsync(cancellationToken);
}
