namespace PromptOps.Domain.Evaluations;

/// <summary>
/// An AI judge's structured assessment of one execution (architecture.md §3) — stored separately
/// from <see cref="HumanEvaluation"/> by requirement (its own table, its own aggregate), even
/// though both describe the same execution from different sources. An independent aggregate:
/// <see cref="ExecutionId"/> is a plain value, not a foreign key — same pattern as
/// <see cref="HumanEvaluation.ExecutionId"/> and <c>EngineeringMetrics.ExecutionId</c>.
///
/// Immutable once recorded and additive: more than one judge run against the same execution
/// produces more than one row, not an upsert — same rationale as the other evaluation/metrics
/// aggregates (a later run should never silently erase an earlier one's judgment).
/// </summary>
public sealed class AIEvaluation : AggregateRoot
{
    public Guid Id { get; }
    public Guid ExecutionId { get; }
    public string JudgeProviderId { get; }
    public string? JudgeModel { get; }
    public bool? SatisfiesAcceptanceCriteria { get; }
    public IReadOnlyList<string> AdrViolations { get; }
    public IReadOnlyList<string> IgnoredRequirements { get; }
    public string? UnnecessaryComplexityNotes { get; }
    public IReadOnlyList<string> SuggestedPromptImprovements { get; }
    public string RawResponse { get; }
    public DateTimeOffset Timestamp { get; }

    private AIEvaluation(
        Guid id,
        Guid executionId,
        string judgeProviderId,
        string? judgeModel,
        bool? satisfiesAcceptanceCriteria,
        IReadOnlyList<string> adrViolations,
        IReadOnlyList<string> ignoredRequirements,
        string? unnecessaryComplexityNotes,
        IReadOnlyList<string> suggestedPromptImprovements,
        string rawResponse,
        DateTimeOffset timestamp)
    {
        Id = id;
        ExecutionId = executionId;
        JudgeProviderId = judgeProviderId;
        JudgeModel = judgeModel;
        SatisfiesAcceptanceCriteria = satisfiesAcceptanceCriteria;
        AdrViolations = adrViolations;
        IgnoredRequirements = ignoredRequirements;
        UnnecessaryComplexityNotes = unnecessaryComplexityNotes;
        SuggestedPromptImprovements = suggestedPromptImprovements;
        RawResponse = rawResponse;
        Timestamp = timestamp;
    }

    public static AIEvaluation Record(
        Guid executionId,
        string judgeProviderId,
        string? judgeModel,
        bool? satisfiesAcceptanceCriteria,
        IReadOnlyList<string> adrViolations,
        IReadOnlyList<string> ignoredRequirements,
        string? unnecessaryComplexityNotes,
        IReadOnlyList<string> suggestedPromptImprovements,
        string rawResponse,
        DateTimeOffset? timestamp = null)
    {
        if (executionId == Guid.Empty)
            throw new ArgumentException("executionId is required.", nameof(executionId));
        if (string.IsNullOrWhiteSpace(judgeProviderId))
            throw new ArgumentException("judgeProviderId is required.", nameof(judgeProviderId));
        ArgumentNullException.ThrowIfNull(rawResponse);

        var id = Guid.NewGuid();
        var recordedAt = timestamp ?? DateTimeOffset.UtcNow;

        var evaluation = new AIEvaluation(
            id, executionId, judgeProviderId, judgeModel, satisfiesAcceptanceCriteria,
            adrViolations, ignoredRequirements, unnecessaryComplexityNotes,
            suggestedPromptImprovements, rawResponse, recordedAt);

        evaluation.AddDomainEvent(new AIEvaluationRecorded(id, executionId, judgeProviderId, recordedAt));
        return evaluation;
    }

    /// <summary>Reconstructs a persisted evaluation (e.g. by a repository) — no domain event.</summary>
    public static AIEvaluation Rehydrate(
        Guid id,
        Guid executionId,
        string judgeProviderId,
        string? judgeModel,
        bool? satisfiesAcceptanceCriteria,
        IReadOnlyList<string> adrViolations,
        IReadOnlyList<string> ignoredRequirements,
        string? unnecessaryComplexityNotes,
        IReadOnlyList<string> suggestedPromptImprovements,
        string rawResponse,
        DateTimeOffset timestamp) => new(
            id, executionId, judgeProviderId, judgeModel, satisfiesAcceptanceCriteria,
            adrViolations, ignoredRequirements, unnecessaryComplexityNotes,
            suggestedPromptImprovements, rawResponse, timestamp);
}
