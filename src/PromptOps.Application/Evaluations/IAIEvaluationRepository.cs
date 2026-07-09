using PromptOps.Domain.Evaluations;

namespace PromptOps.Application.Evaluations;

public interface IAIEvaluationRepository
{
    Task AddAsync(AIEvaluation evaluation, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AIEvaluation>> GetByExecutionIdAsync(Guid executionId, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
