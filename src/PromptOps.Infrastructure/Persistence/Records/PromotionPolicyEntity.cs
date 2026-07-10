namespace PromptOps.Infrastructure.Persistence.Records;

/// <summary>EF Core persistence shape for <see cref="PromptOps.Domain.Promotion.PromotionPolicy"/> — a single row, no name/version columns (unlike <c>ScoringConfigEntity</c>) since this is a global settings singleton, not a named/versioned methodology.</summary>
public sealed class PromotionPolicyEntity
{
    public Guid Id { get; set; }
    public bool RequireHumanEvaluation { get; set; }
    public bool AutoPromotionEnabled { get; set; }
    public double? MinimumScoreThreshold { get; set; }
    public double? MinimumMarginOverActive { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
