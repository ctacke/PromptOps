namespace PromptOps.Infrastructure.Persistence.Records;

/// <summary>EF Core persistence shape for <see cref="PromptOps.Domain.Refinement.RefinementPolicy"/> — a single settings row, same shape rationale as <c>PromotionPolicyEntity</c>.</summary>
public sealed class RefinementPolicyEntity
{
    public Guid Id { get; set; }
    public bool AutoRefinementEnabled { get; set; }
    public int SyntheticSampleSize { get; set; }
    public double MinQualityDelta { get; set; }
    public double AbExplorationRate { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
