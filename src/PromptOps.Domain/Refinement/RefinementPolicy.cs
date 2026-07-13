namespace PromptOps.Domain.Refinement;

/// <summary>
/// A single global, mutable settings object governing automatic prompt refinement (Phase 16) —
/// same "operational on/off knob, not a versioned methodology" rationale as
/// <c>PromotionPolicy</c> (Phase 11) and <c>AIEvaluationPolicy</c> (Phase 12/13), so it is a single
/// mutable row rather than an immutable/versioned config. See docs/prompt-refinement.md.
///
/// <list type="bullet">
/// <item><see cref="AutoRefinementEnabled"/> (Phase 16a) — the gate for drafting an improved version
/// from an AI judge's suggestions.</item>
/// <item><see cref="SyntheticSampleSize"/> / <see cref="MinQualityDelta"/> (Phase 16b) — the
/// synthetic-benchmark pre-screen: how many generated scenarios to grade a draft against, and how
/// much it must beat the active version by before it becomes A/B-eligible. A sample size of 0 (the
/// default) disables benchmarking, so a fresh draft stays pending manual review rather than being
/// auto-adopted.</item>
/// <item><see cref="AbExplorationRate"/> (Phase 16c) — the fraction of matching sessions that are
/// routed to an A/B-eligible draft instead of the active version, so the draft earns a real score
/// from live traffic and the existing auto-promotion gate can promote it on real evidence. 0 (the
/// default) disables shadow traffic entirely.</item>
/// </list>
/// </summary>
public sealed class RefinementPolicy
{
    public Guid Id { get; }
    public bool AutoRefinementEnabled { get; private set; }
    public int SyntheticSampleSize { get; private set; }
    public double MinQualityDelta { get; private set; }
    public double AbExplorationRate { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private RefinementPolicy(Guid id, bool autoRefinementEnabled, int syntheticSampleSize, double minQualityDelta, double abExplorationRate, DateTimeOffset updatedAt)
    {
        Id = id;
        AutoRefinementEnabled = autoRefinementEnabled;
        SyntheticSampleSize = syntheticSampleSize;
        MinQualityDelta = minQualityDelta;
        AbExplorationRate = abExplorationRate;
        UpdatedAt = updatedAt;
    }

    /// <summary>Today's default behavior, preserved exactly: automatic refinement off, benchmarking off, no shadow traffic.</summary>
    public static RefinementPolicy CreateDefault(DateTimeOffset? now = null)
        => new(Guid.NewGuid(), autoRefinementEnabled: false, syntheticSampleSize: 0, minQualityDelta: 0, abExplorationRate: 0, now ?? DateTimeOffset.UtcNow);

    /// <summary>Reconstructs a persisted policy (e.g. by a repository) — no validation, matching every other aggregate's Rehydrate.</summary>
    public static RefinementPolicy Rehydrate(Guid id, bool autoRefinementEnabled, int syntheticSampleSize, double minQualityDelta, double abExplorationRate, DateTimeOffset updatedAt)
        => new(id, autoRefinementEnabled, syntheticSampleSize, minQualityDelta, abExplorationRate, updatedAt);

    public void Update(bool autoRefinementEnabled, int syntheticSampleSize, double minQualityDelta, double abExplorationRate, DateTimeOffset? updatedAt = null)
    {
        if (syntheticSampleSize < 0)
            throw new ArgumentOutOfRangeException(nameof(syntheticSampleSize), syntheticSampleSize, "must be non-negative (0 disables benchmarking).");
        if (minQualityDelta < 0)
            throw new ArgumentOutOfRangeException(nameof(minQualityDelta), minQualityDelta, "must be non-negative.");
        if (abExplorationRate is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(abExplorationRate), abExplorationRate, "must be between 0 and 1 (0 disables shadow traffic).");

        AutoRefinementEnabled = autoRefinementEnabled;
        SyntheticSampleSize = syntheticSampleSize;
        MinQualityDelta = minQualityDelta;
        AbExplorationRate = abExplorationRate;
        UpdatedAt = updatedAt ?? DateTimeOffset.UtcNow;
    }
}
