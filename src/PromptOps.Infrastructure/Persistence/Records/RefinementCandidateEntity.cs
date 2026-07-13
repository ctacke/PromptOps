namespace PromptOps.Infrastructure.Persistence.Records;

/// <summary>EF Core persistence shape for <see cref="PromptOps.Domain.Refinement.RefinementCandidate"/> (Phase 16b) — one row per auto-refined Draft tracked through the benchmark gate.</summary>
public sealed class RefinementCandidateEntity
{
    public Guid Id { get; set; }
    public Guid PromptId { get; set; }
    public Guid DraftVersionId { get; set; }
    public Guid ActiveVersionId { get; set; }

    /// <summary>Enum name (PendingBenchmark/AbEligible/Rejected) — same string-column convention as <c>ExecutionRecordEntity.Status</c>.</summary>
    public string Status { get; set; } = string.Empty;
    public double? ActiveScore { get; set; }
    public double? CandidateScore { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? EvaluatedAt { get; set; }
}
