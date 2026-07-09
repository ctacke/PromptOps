namespace PromptOps.Infrastructure.Persistence.Records;

/// <summary>EF Core persistence shape for <see cref="PromptOps.Domain.Scoring.ScoringConfig"/> — the weights are flattened to columns rather than a JSON blob since they're a fixed, named shape (<see cref="PromptOps.Domain.Scoring.ScoringWeights"/>), not an open dictionary.</summary>
public sealed class ScoringConfigEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public double WeightHumanRating { get; set; }
    public double WeightSonar { get; set; }
    public double WeightTests { get; set; }
    public double WeightBuild { get; set; }
    public double WeightAcceptanceCriteria { get; set; }
    public double WeightManualFixes { get; set; }
    public double WeightReviewComments { get; set; }
    public double WeightRegressionBugs { get; set; }
}
