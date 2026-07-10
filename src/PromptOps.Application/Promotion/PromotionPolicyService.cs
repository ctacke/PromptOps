using PromptOps.Domain.Promotion;

namespace PromptOps.Application.Promotion;

/// <summary>Application-layer use cases for the promotion policy (Phase 11) — the config that governs whether human evaluation is required and whether scores can auto-promote a version.</summary>
public sealed class PromotionPolicyService(IPromotionPolicyRepository repository)
{
    /// <summary>
    /// Loads the one policy row, lazy-bootstrapping the default (require human evaluation,
    /// auto-promotion off — today's existing behavior, unchanged) on a fresh daemon that's never
    /// configured one. Same pattern as <c>ScoringService.ResolveConfigAsync</c>.
    /// </summary>
    public async Task<PromotionPolicy> GetOrCreateDefaultAsync(CancellationToken cancellationToken = default)
    {
        var existing = await repository.GetAsync(cancellationToken);
        if (existing is not null)
            return existing;

        var created = PromotionPolicy.CreateDefault();
        await repository.AddAsync(created, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        return created;
    }

    public async Task<PromotionPolicy> UpdateAsync(
        bool requireHumanEvaluation,
        bool autoPromotionEnabled,
        double? minimumScoreThreshold,
        double? minimumMarginOverActive,
        CancellationToken cancellationToken = default)
    {
        var policy = await GetOrCreateDefaultAsync(cancellationToken);

        policy.Update(requireHumanEvaluation, autoPromotionEnabled, minimumScoreThreshold, minimumMarginOverActive);

        await repository.UpdateAsync(policy, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        return policy;
    }
}
