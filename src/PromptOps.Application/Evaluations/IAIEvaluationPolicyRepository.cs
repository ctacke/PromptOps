using PromptOps.Domain.Evaluations;

namespace PromptOps.Application.Evaluations;

/// <summary>Persistence port for the single global <see cref="AIEvaluationPolicy"/> singleton.</summary>
public interface IAIEvaluationPolicyRepository
{
    /// <summary>The one policy row, if it's ever been created. <c>null</c> on a fresh daemon — <see cref="AIEvaluationPolicyService"/> lazy-bootstraps a default the same way <c>PromotionPolicyService</c> does.</summary>
    Task<AIEvaluationPolicy?> GetAsync(CancellationToken cancellationToken = default);

    Task AddAsync(AIEvaluationPolicy policy, CancellationToken cancellationToken = default);

    Task UpdateAsync(AIEvaluationPolicy policy, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
