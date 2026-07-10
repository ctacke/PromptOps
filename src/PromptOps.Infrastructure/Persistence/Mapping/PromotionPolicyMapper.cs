using PromptOps.Domain.Promotion;
using PromptOps.Infrastructure.Persistence.Records;

namespace PromptOps.Infrastructure.Persistence.Mapping;

internal static class PromotionPolicyMapper
{
    public static PromotionPolicyEntity ToNewEntity(PromotionPolicy policy) => new()
    {
        Id = policy.Id,
        RequireHumanEvaluation = policy.RequireHumanEvaluation,
        AutoPromotionEnabled = policy.AutoPromotionEnabled,
        MinimumScoreThreshold = policy.MinimumScoreThreshold,
        MinimumMarginOverActive = policy.MinimumMarginOverActive,
        UpdatedAt = policy.UpdatedAt
    };

    public static void ApplyChanges(PromotionPolicyEntity entity, PromotionPolicy policy)
    {
        entity.RequireHumanEvaluation = policy.RequireHumanEvaluation;
        entity.AutoPromotionEnabled = policy.AutoPromotionEnabled;
        entity.MinimumScoreThreshold = policy.MinimumScoreThreshold;
        entity.MinimumMarginOverActive = policy.MinimumMarginOverActive;
        entity.UpdatedAt = policy.UpdatedAt;
    }

    public static PromotionPolicy ToDomain(PromotionPolicyEntity entity) => PromotionPolicy.Rehydrate(
        entity.Id,
        entity.RequireHumanEvaluation,
        entity.AutoPromotionEnabled,
        entity.MinimumScoreThreshold,
        entity.MinimumMarginOverActive,
        entity.UpdatedAt);
}
