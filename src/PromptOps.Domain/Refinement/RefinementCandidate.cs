namespace PromptOps.Domain.Refinement;

/// <summary>The lifecycle state of an auto-refined Draft as it goes through the synthetic-benchmark pre-screen (Phase 16b).</summary>
public enum RefinementCandidateStatus
{
    /// <summary>Drafted but not yet benchmarked (or benchmarking is disabled / was inconclusive) — awaits manual review, not auto-adopted.</summary>
    PendingBenchmark,

    /// <summary>Passed the synthetic benchmark (beat the active version by the required margin) — eligible for A/B shadow traffic (Phase 16c).</summary>
    AbEligible,

    /// <summary>Failed the synthetic benchmark — the underlying Draft version is deprecated and never reaches real work.</summary>
    Rejected
}

/// <summary>
/// Tracks one auto-refined Draft (Phase 16b) through the synthetic-benchmark gate: its lineage
/// (which prompt, which draft, which active baseline it was compared against), the benchmark scores,
/// and whether it became A/B-eligible or was rejected. Persisting this — rather than inferring
/// eligibility from <c>PromptVersion.Status</c> alone — lets Phase 16c reliably distinguish a
/// benchmark-passing auto-refinement from an arbitrary human-authored Draft, and keeps the scores
/// for observability.
/// </summary>
public sealed class RefinementCandidate
{
    public Guid Id { get; }
    public Guid PromptId { get; }
    public Guid DraftVersionId { get; }
    public Guid ActiveVersionId { get; }
    public RefinementCandidateStatus Status { get; private set; }
    public double? ActiveScore { get; private set; }
    public double? CandidateScore { get; private set; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset? EvaluatedAt { get; private set; }

    private RefinementCandidate(
        Guid id, Guid promptId, Guid draftVersionId, Guid activeVersionId,
        RefinementCandidateStatus status, double? activeScore, double? candidateScore,
        DateTimeOffset createdAt, DateTimeOffset? evaluatedAt)
    {
        Id = id;
        PromptId = promptId;
        DraftVersionId = draftVersionId;
        ActiveVersionId = activeVersionId;
        Status = status;
        ActiveScore = activeScore;
        CandidateScore = candidateScore;
        CreatedAt = createdAt;
        EvaluatedAt = evaluatedAt;
    }

    public static RefinementCandidate Create(Guid promptId, Guid draftVersionId, Guid activeVersionId, DateTimeOffset? createdAt = null)
    {
        if (promptId == Guid.Empty) throw new ArgumentException("promptId is required.", nameof(promptId));
        if (draftVersionId == Guid.Empty) throw new ArgumentException("draftVersionId is required.", nameof(draftVersionId));
        if (activeVersionId == Guid.Empty) throw new ArgumentException("activeVersionId is required.", nameof(activeVersionId));

        return new RefinementCandidate(
            Guid.NewGuid(), promptId, draftVersionId, activeVersionId,
            RefinementCandidateStatus.PendingBenchmark, null, null, createdAt ?? DateTimeOffset.UtcNow, null);
    }

    /// <summary>Reconstructs a persisted candidate (e.g. by a repository) — no validation.</summary>
    public static RefinementCandidate Rehydrate(
        Guid id, Guid promptId, Guid draftVersionId, Guid activeVersionId,
        RefinementCandidateStatus status, double? activeScore, double? candidateScore,
        DateTimeOffset createdAt, DateTimeOffset? evaluatedAt)
        => new(id, promptId, draftVersionId, activeVersionId, status, activeScore, candidateScore, createdAt, evaluatedAt);

    public void MarkEligible(double activeScore, double candidateScore, DateTimeOffset? evaluatedAt = null)
        => Resolve(RefinementCandidateStatus.AbEligible, activeScore, candidateScore, evaluatedAt);

    public void Reject(double activeScore, double candidateScore, DateTimeOffset? evaluatedAt = null)
        => Resolve(RefinementCandidateStatus.Rejected, activeScore, candidateScore, evaluatedAt);

    private void Resolve(RefinementCandidateStatus status, double activeScore, double candidateScore, DateTimeOffset? evaluatedAt)
    {
        if (Status != RefinementCandidateStatus.PendingBenchmark)
            throw new InvalidOperationException($"Candidate '{Id}' is already resolved ({Status}).");

        Status = status;
        ActiveScore = activeScore;
        CandidateScore = candidateScore;
        EvaluatedAt = evaluatedAt ?? DateTimeOffset.UtcNow;
    }
}
