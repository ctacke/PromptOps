namespace PromptOps.Domain.Promotion;

/// <summary>
/// A single global, mutable settings object — deliberately not versioned/immutable like
/// <c>ScoringConfig</c> (Phase 8). <c>ScoringConfig</c> is versioned because a past
/// <c>PromptScore</c> must keep meaning exactly what it meant when it was computed
/// (reproducibility). This is an operational on/off knob with no such requirement — more like a
/// feature flag than a scoring methodology — so versioning it would be ceremony without payoff.
/// See docs/promotion-policy.md.
/// </summary>
public sealed class PromotionPolicy
{
    public Guid Id { get; }
    public bool RequireHumanEvaluation { get; private set; }
    public bool AutoPromotionEnabled { get; private set; }
    public double? MinimumScoreThreshold { get; private set; }
    public double? MinimumMarginOverActive { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private PromotionPolicy(
        Guid id,
        bool requireHumanEvaluation,
        bool autoPromotionEnabled,
        double? minimumScoreThreshold,
        double? minimumMarginOverActive,
        DateTimeOffset updatedAt)
    {
        Id = id;
        RequireHumanEvaluation = requireHumanEvaluation;
        AutoPromotionEnabled = autoPromotionEnabled;
        MinimumScoreThreshold = minimumScoreThreshold;
        MinimumMarginOverActive = minimumMarginOverActive;
        UpdatedAt = updatedAt;
    }

    /// <summary>Today's default behavior, preserved exactly: human evaluation required, auto-promotion off.</summary>
    public static PromotionPolicy CreateDefault(DateTimeOffset? now = null)
        => new(Guid.NewGuid(), requireHumanEvaluation: true, autoPromotionEnabled: false, null, null, now ?? DateTimeOffset.UtcNow);

    /// <summary>Reconstructs a persisted policy (e.g. by a repository) — no validation, matching every other aggregate's Rehydrate.</summary>
    public static PromotionPolicy Rehydrate(
        Guid id,
        bool requireHumanEvaluation,
        bool autoPromotionEnabled,
        double? minimumScoreThreshold,
        double? minimumMarginOverActive,
        DateTimeOffset updatedAt)
        => new(id, requireHumanEvaluation, autoPromotionEnabled, minimumScoreThreshold, minimumMarginOverActive, updatedAt);

    public void Update(
        bool requireHumanEvaluation,
        bool autoPromotionEnabled,
        double? minimumScoreThreshold,
        double? minimumMarginOverActive,
        DateTimeOffset? updatedAt = null)
    {
        if (autoPromotionEnabled && requireHumanEvaluation)
            throw new InvalidOperationException("AutoPromotionEnabled requires RequireHumanEvaluation to be false — auto-promotion is what replaces the human sign-off step.");
        if (autoPromotionEnabled && minimumScoreThreshold is null && minimumMarginOverActive is null)
            throw new InvalidOperationException("AutoPromotionEnabled requires at least one of MinimumScoreThreshold or MinimumMarginOverActive.");
        if (minimumScoreThreshold is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(minimumScoreThreshold), minimumScoreThreshold, "must be between 0 and 100.");
        if (minimumMarginOverActive is < 0)
            throw new ArgumentOutOfRangeException(nameof(minimumMarginOverActive), minimumMarginOverActive, "must be non-negative.");

        RequireHumanEvaluation = requireHumanEvaluation;
        AutoPromotionEnabled = autoPromotionEnabled;
        MinimumScoreThreshold = minimumScoreThreshold;
        MinimumMarginOverActive = minimumMarginOverActive;
        UpdatedAt = updatedAt ?? DateTimeOffset.UtcNow;
    }
}
