using PromptOps.Domain.Evaluations;

namespace PromptOps.Application.Evaluations;

public interface IAIEvaluationRepository
{
    Task AddAsync(AIEvaluation evaluation, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AIEvaluation>> GetByExecutionIdAsync(Guid executionId, CancellationToken cancellationToken = default);

    /// <summary>Total count across every execution, computed in SQL.</summary>
    Task<int> GetCountAsync(CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
