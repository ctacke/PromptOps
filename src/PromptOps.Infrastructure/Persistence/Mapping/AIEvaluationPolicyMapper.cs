using PromptOps.Domain.Evaluations;
using PromptOps.Infrastructure.Persistence.Records;

namespace PromptOps.Infrastructure.Persistence.Mapping;

internal static class AIEvaluationPolicyMapper
{
    public static AIEvaluationPolicyEntity ToNewEntity(AIEvaluationPolicy policy) => new()
    {
        Id = policy.Id,
        AutoEvaluateOnFinish = policy.AutoEvaluateOnFinish,
        UpdatedAt = policy.UpdatedAt
    };

    public static void ApplyChanges(AIEvaluationPolicyEntity entity, AIEvaluationPolicy policy)
    {
        entity.AutoEvaluateOnFinish = policy.AutoEvaluateOnFinish;
        entity.UpdatedAt = policy.UpdatedAt;
    }

    public static AIEvaluationPolicy ToDomain(AIEvaluationPolicyEntity entity) => AIEvaluationPolicy.Rehydrate(
        entity.Id, entity.AutoEvaluateOnFinish, entity.UpdatedAt);
}
