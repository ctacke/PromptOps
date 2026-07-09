namespace PromptOps.Domain.Evaluations;

/// <summary>
/// A developer's structured rating of one execution (architecture.md §3). An independent
/// aggregate from <c>ExecutionRecord</c>: <see cref="ExecutionId"/> is a plain value, not a
/// foreign key — same pattern as <c>EngineeringMetrics.ExecutionId</c>
/// (docs/metrics.md) — since submitting a rating never requires the execution to be loaded in
/// the same transaction.
///
/// Immutable once submitted and additive: more than one evaluator (or the same evaluator, twice)
/// can rate the same execution — each submission is its own row, not an upsert. Reconciling
/// multiple ratings into one number is Phase 8's job (<c>IScoringProvider</c>), not this layer's.
/// </summary>
public sealed class HumanEvaluation : AggregateRoot
{
    private const int MinRating = 1;
    private const int MaxRating = 5;

    public Guid Id { get; }
    public Guid ExecutionId { get; }
    public string EvaluatorId { get; }
    public int Correctness { get; }
    public int Helpfulness { get; }
    public int Architecture { get; }
    public int Readability { get; }
    public int Completeness { get; }
    public bool Hallucinations { get; }
    public int Confidence { get; }
    public int OverallSatisfaction { get; }
    public string? Notes { get; }
    public DateTimeOffset Timestamp { get; }

    private HumanEvaluation(
        Guid id,
        Guid executionId,
        string evaluatorId,
        int correctness,
        int helpfulness,
        int architecture,
        int readability,
        int completeness,
        bool hallucinations,
        int confidence,
        int overallSatisfaction,
        string? notes,
        DateTimeOffset timestamp)
    {
        Id = id;
        ExecutionId = executionId;
        EvaluatorId = evaluatorId;
        Correctness = correctness;
        Helpfulness = helpfulness;
        Architecture = architecture;
        Readability = readability;
        Completeness = completeness;
        Hallucinations = hallucinations;
        Confidence = confidence;
        OverallSatisfaction = overallSatisfaction;
        Notes = notes;
        Timestamp = timestamp;
    }

    public static HumanEvaluation Submit(
        Guid executionId,
        string evaluatorId,
        int correctness,
        int helpfulness,
        int architecture,
        int readability,
        int completeness,
        bool hallucinations,
        int confidence,
        int overallSatisfaction,
        string? notes = null,
        DateTimeOffset? timestamp = null)
    {
        if (executionId == Guid.Empty)
            throw new ArgumentException("executionId is required.", nameof(executionId));
        if (string.IsNullOrWhiteSpace(evaluatorId))
            throw new ArgumentException("evaluatorId is required.", nameof(evaluatorId));

        ValidateRating(correctness, nameof(correctness));
        ValidateRating(helpfulness, nameof(helpfulness));
        ValidateRating(architecture, nameof(architecture));
        ValidateRating(readability, nameof(readability));
        ValidateRating(completeness, nameof(completeness));
        ValidateRating(confidence, nameof(confidence));
        ValidateRating(overallSatisfaction, nameof(overallSatisfaction));

        var id = Guid.NewGuid();
        var submittedAt = timestamp ?? DateTimeOffset.UtcNow;

        var evaluation = new HumanEvaluation(
            id, executionId, evaluatorId, correctness, helpfulness, architecture, readability,
            completeness, hallucinations, confidence, overallSatisfaction, notes, submittedAt);

        evaluation.AddDomainEvent(new HumanEvaluationSubmitted(id, executionId, evaluatorId, submittedAt));
        return evaluation;
    }

    /// <summary>Reconstructs a persisted evaluation (e.g. by a repository) — no domain event.</summary>
    public static HumanEvaluation Rehydrate(
        Guid id,
        Guid executionId,
        string evaluatorId,
        int correctness,
        int helpfulness,
        int architecture,
        int readability,
        int completeness,
        bool hallucinations,
        int confidence,
        int overallSatisfaction,
        string? notes,
        DateTimeOffset timestamp) => new(
            id, executionId, evaluatorId, correctness, helpfulness, architecture, readability,
            completeness, hallucinations, confidence, overallSatisfaction, notes, timestamp);

    private static void ValidateRating(int value, string paramName)
    {
        if (value < MinRating || value > MaxRating)
            throw new ArgumentOutOfRangeException(paramName, value, $"Rating must be between {MinRating} and {MaxRating}.");
    }
}
