namespace PromptOps.Domain.Scoring;

/// <summary>
/// A computed score for a <c>PromptVersion</c>, aggregated across every execution of it
/// (architecture.md §3) — not per-execution, unlike <c>EngineeringMetrics</c>/<c>HumanEvaluation</c>/
/// <c>AIEvaluation</c>. <see cref="PromptVersionId"/> and <see cref="ScoringConfigId"/> are plain
/// values, not foreign keys, the same pattern used throughout (docs/metrics.md).
///
/// Immutable and additive: every recompute — on-demand or debounced-on-event — produces a new
/// row rather than updating one in place, so score trends over time are observable (a stated
/// Phase 11+ observability target) and a score always means exactly what it meant when computed.
/// </summary>
public sealed class PromptScore : AggregateRoot
{
    public Guid Id { get; }
    public Guid PromptVersionId { get; }
    public Guid ScoringConfigId { get; }
    public DateTimeOffset ComputedAt { get; }
    public double OverallScore { get; }
    public IReadOnlyDictionary<string, double> ComponentScores { get; }
    public int SampleSize { get; }

    private PromptScore(
        Guid id,
        Guid promptVersionId,
        Guid scoringConfigId,
        DateTimeOffset computedAt,
        double overallScore,
        IReadOnlyDictionary<string, double> componentScores,
        int sampleSize)
    {
        Id = id;
        PromptVersionId = promptVersionId;
        ScoringConfigId = scoringConfigId;
        ComputedAt = computedAt;
        OverallScore = overallScore;
        ComponentScores = componentScores;
        SampleSize = sampleSize;
    }

    public static PromptScore Compute(
        Guid promptVersionId,
        Guid scoringConfigId,
        double overallScore,
        IReadOnlyDictionary<string, double> componentScores,
        int sampleSize,
        DateTimeOffset? computedAt = null)
    {
        if (promptVersionId == Guid.Empty)
            throw new ArgumentException("promptVersionId is required.", nameof(promptVersionId));
        if (scoringConfigId == Guid.Empty)
            throw new ArgumentException("scoringConfigId is required.", nameof(scoringConfigId));
        if (sampleSize < 0)
            throw new ArgumentOutOfRangeException(nameof(sampleSize), sampleSize, "sampleSize cannot be negative.");
        ArgumentNullException.ThrowIfNull(componentScores);

        var id = Guid.NewGuid();
        var timestamp = computedAt ?? DateTimeOffset.UtcNow;

        var score = new PromptScore(id, promptVersionId, scoringConfigId, timestamp, overallScore, componentScores, sampleSize);
        score.AddDomainEvent(new ScoreComputed(id, promptVersionId, scoringConfigId, overallScore, timestamp));
        return score;
    }

    /// <summary>Reconstructs a persisted score (e.g. by a repository) — no domain event.</summary>
    public static PromptScore Rehydrate(
        Guid id,
        Guid promptVersionId,
        Guid scoringConfigId,
        DateTimeOffset computedAt,
        double overallScore,
        IReadOnlyDictionary<string, double> componentScores,
        int sampleSize) => new(id, promptVersionId, scoringConfigId, computedAt, overallScore, componentScores, sampleSize);
}
