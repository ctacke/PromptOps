using PromptOps.Domain.Evaluations;

namespace PromptOps.Application.Evaluations;

public interface IHumanEvaluationRepository
{
    Task AddAsync(HumanEvaluation evaluation, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<HumanEvaluation>> GetByExecutionIdAsync(Guid executionId, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
