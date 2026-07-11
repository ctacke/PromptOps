using PromptOps.Domain.Evaluations;

namespace PromptOps.Application.Evaluations;

public interface IHumanEvaluationRepository
{
    Task AddAsync(HumanEvaluation evaluation, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<HumanEvaluation>> GetByExecutionIdAsync(Guid executionId, CancellationToken cancellationToken = default);

    /// <summary>Total count across every execution, computed in SQL.</summary>
    Task<int> GetCountAsync(CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
