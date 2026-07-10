namespace PromptOps.Infrastructure.Persistence.Records;

/// <summary>EF Core persistence shape for <see cref="PromptOps.Domain.Evaluations.AIEvaluationPolicy"/> — a single row, same singleton-settings pattern as <c>PromotionPolicyEntity</c>.</summary>
public sealed class AIEvaluationPolicyEntity
{
    public Guid Id { get; set; }
    public bool AutoEvaluateOnFinish { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
