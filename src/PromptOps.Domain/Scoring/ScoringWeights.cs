namespace PromptOps.Domain.Scoring;

/// <summary>
/// The fixed set of weighted inputs a <see cref="ScoringConfig"/> combines (architecture.md §3).
/// Every property defaults to 0 — a config that doesn't set a given weight excludes that
/// component entirely rather than treating it as present-with-zero-value (architecture.md §6:
/// "Existing configs keep working (missing weight = 0 contribution)").
/// </summary>
public sealed record ScoringWeights
{
    public double HumanRating { get; init; }
    public double Sonar { get; init; }
    public double Tests { get; init; }
    public double Build { get; init; }
    public double AcceptanceCriteria { get; init; }
    public double ManualFixes { get; init; }
    public double ReviewComments { get; init; }
    public double RegressionBugs { get; init; }
}
