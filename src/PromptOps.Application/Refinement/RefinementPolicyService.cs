using PromptOps.Domain.Refinement;

namespace PromptOps.Application.Refinement;

/// <summary>Application-layer use cases for the refinement policy (Phase 16) — the on/off switch <c>PromptRefinementTrigger</c> reads before drafting an improved version.</summary>
public sealed class RefinementPolicyService(IRefinementPolicyRepository repository)
{
    /// <summary>Loads the one policy row, lazy-bootstrapping the default (automatic refinement off — today's behavior, unchanged) on a fresh daemon. Same pattern as <c>PromotionPolicyService.GetOrCreateDefaultAsync</c>.</summary>
    public async Task<RefinementPolicy> GetOrCreateDefaultAsync(CancellationToken cancellationToken = default)
    {
        var existing = await repository.GetAsync(cancellationToken);
        if (existing is not null)
            return existing;

        var created = RefinementPolicy.CreateDefault();
        await repository.AddAsync(created, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        return created;
    }

    public async Task<RefinementPolicy> UpdateAsync(
        bool autoRefinementEnabled,
        int syntheticSampleSize,
        double minQualityDelta,
        double abExplorationRate,
        CancellationToken cancellationToken = default)
    {
        var policy = await GetOrCreateDefaultAsync(cancellationToken);

        policy.Update(autoRefinementEnabled, syntheticSampleSize, minQualityDelta, abExplorationRate);

        await repository.UpdateAsync(policy, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        return policy;
    }
}
