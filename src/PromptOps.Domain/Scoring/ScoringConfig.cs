namespace PromptOps.Domain.Scoring;

/// <summary>
/// A named, versioned set of scoring weights (architecture.md §3). Immutable once created —
/// "changing the weights" means creating a new version under the same name, never mutating an
/// existing row, which is what makes <see cref="PromptScore.ScoringConfigId"/> a reproducibility
/// guarantee: a score computed under version 3 keeps meaning exactly what it meant even after
/// version 4 exists.
/// </summary>
public sealed class ScoringConfig
{
    public Guid Id { get; }
    public string Name { get; }
    public int Version { get; }
    public ScoringWeights Weights { get; }
    public DateTimeOffset CreatedAt { get; }

    private ScoringConfig(Guid id, string name, int version, ScoringWeights weights, DateTimeOffset createdAt)
    {
        Id = id;
        Name = name;
        Version = version;
        Weights = weights;
        CreatedAt = createdAt;
    }

    public static ScoringConfig Create(string name, int version, ScoringWeights weights, DateTimeOffset? createdAt = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("name is required.", nameof(name));
        if (version < 1)
            throw new ArgumentOutOfRangeException(nameof(version), version, "version must be >= 1.");
        ArgumentNullException.ThrowIfNull(weights);
        ValidateNonNegative(weights);

        return new ScoringConfig(Guid.NewGuid(), name, version, weights, createdAt ?? DateTimeOffset.UtcNow);
    }

    /// <summary>Reconstructs a persisted config (e.g. by a repository).</summary>
    public static ScoringConfig Rehydrate(Guid id, string name, int version, ScoringWeights weights, DateTimeOffset createdAt)
        => new(id, name, version, weights, createdAt);

    private static void ValidateNonNegative(ScoringWeights weights)
    {
        foreach (var (weightName, value) in new[]
        {
            (nameof(weights.HumanRating), weights.HumanRating),
            (nameof(weights.Sonar), weights.Sonar),
            (nameof(weights.Tests), weights.Tests),
            (nameof(weights.Build), weights.Build),
            (nameof(weights.AcceptanceCriteria), weights.AcceptanceCriteria),
            (nameof(weights.ManualFixes), weights.ManualFixes),
            (nameof(weights.ReviewComments), weights.ReviewComments),
            (nameof(weights.RegressionBugs), weights.RegressionBugs)
        })
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(weights), value, $"{weightName} weight cannot be negative.");
        }
    }
}
