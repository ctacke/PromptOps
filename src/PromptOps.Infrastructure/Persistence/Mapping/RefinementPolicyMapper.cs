using PromptOps.Domain.Refinement;
using PromptOps.Infrastructure.Persistence.Records;

namespace PromptOps.Infrastructure.Persistence.Mapping;

internal static class RefinementPolicyMapper
{
    public static RefinementPolicyEntity ToNewEntity(RefinementPolicy policy) => new()
    {
        Id = policy.Id,
        AutoRefinementEnabled = policy.AutoRefinementEnabled,
        SyntheticSampleSize = policy.SyntheticSampleSize,
        MinQualityDelta = policy.MinQualityDelta,
        AbExplorationRate = policy.AbExplorationRate,
        UpdatedAt = policy.UpdatedAt
    };

    public static void ApplyChanges(RefinementPolicyEntity entity, RefinementPolicy policy)
    {
        entity.AutoRefinementEnabled = policy.AutoRefinementEnabled;
        entity.SyntheticSampleSize = policy.SyntheticSampleSize;
        entity.MinQualityDelta = policy.MinQualityDelta;
        entity.AbExplorationRate = policy.AbExplorationRate;
        entity.UpdatedAt = policy.UpdatedAt;
    }

    public static RefinementPolicy ToDomain(RefinementPolicyEntity entity) => RefinementPolicy.Rehydrate(
        entity.Id,
        entity.AutoRefinementEnabled,
        entity.SyntheticSampleSize,
        entity.MinQualityDelta,
        entity.AbExplorationRate,
        entity.UpdatedAt);
}
