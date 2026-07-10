using PromptOps.Domain.Evaluations;

namespace PromptOps.Application.Evaluations;

/// <summary>Application-layer use cases for the AI evaluation policy — the config that governs whether the AI judge runs automatically when an execution finishes.</summary>
public sealed class AIEvaluationPolicyService(IAIEvaluationPolicyRepository repository)
{
    /// <summary>Loads the one policy row, lazy-bootstrapping the default (auto-evaluate off — today's existing behavior, unchanged) on a fresh daemon that's never configured one. Same pattern as <c>PromotionPolicyService.GetOrCreateDefaultAsync</c>.</summary>
    public async Task<AIEvaluationPolicy> GetOrCreateDefaultAsync(CancellationToken cancellationToken = default)
    {
        var existing = await repository.GetAsync(cancellationToken);
        if (existing is not null)
            return existing;

        var created = AIEvaluationPolicy.CreateDefault();
        await repository.AddAsync(created, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        return created;
    }

    public async Task<AIEvaluationPolicy> UpdateAsync(
        bool autoEvaluateOnFinish,
        AutoEvaluationMechanism mechanism = AutoEvaluationMechanism.Daemon,
        CancellationToken cancellationToken = default)
    {
        var policy = await GetOrCreateDefaultAsync(cancellationToken);

        policy.Update(autoEvaluateOnFinish, mechanism);

        await repository.UpdateAsync(policy, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        return policy;
    }
}
