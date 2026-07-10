namespace PromptOps.Domain.Evaluations;

/// <summary>
/// A single global, mutable settings object — same shape as <c>PromotionPolicy</c>
/// (<c>PromptOps.Domain.Promotion</c>), just simpler: one setting, no cross-field validation.
/// Governs whether <c>AutoAIEvaluationTrigger</c> (<c>PromptOps.Infrastructure</c>) automatically
/// runs the AI judge when an execution finishes, instead of requiring an explicit
/// <c>POST /executions/{id}/ai-evaluations</c> call. Off by default — the judge is a real LLM call
/// with retries, so auto-firing it on every execution should be an explicit opt-in, not silent
/// default behavior (the same reasoning <c>PromotionPolicy</c> applied to auto-promotion).
/// </summary>
public sealed class AIEvaluationPolicy
{
    public Guid Id { get; }
    public bool AutoEvaluateOnFinish { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private AIEvaluationPolicy(Guid id, bool autoEvaluateOnFinish, DateTimeOffset updatedAt)
    {
        Id = id;
        AutoEvaluateOnFinish = autoEvaluateOnFinish;
        UpdatedAt = updatedAt;
    }

    public static AIEvaluationPolicy CreateDefault(DateTimeOffset? now = null)
        => new(Guid.NewGuid(), autoEvaluateOnFinish: false, now ?? DateTimeOffset.UtcNow);

    /// <summary>Reconstructs a persisted policy (e.g. by a repository) — no validation, matching every other aggregate's Rehydrate.</summary>
    public static AIEvaluationPolicy Rehydrate(Guid id, bool autoEvaluateOnFinish, DateTimeOffset updatedAt)
        => new(id, autoEvaluateOnFinish, updatedAt);

    public void Update(bool autoEvaluateOnFinish, DateTimeOffset? updatedAt = null)
    {
        AutoEvaluateOnFinish = autoEvaluateOnFinish;
        UpdatedAt = updatedAt ?? DateTimeOffset.UtcNow;
    }
}
