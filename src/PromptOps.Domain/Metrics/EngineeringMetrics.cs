namespace PromptOps.Domain.Metrics;

/// <summary>
/// One engineering metric source's report against an execution (Sonar, build/test results,
/// review activity — ADR-0003 <c>IMetricCollector</c>). An independent aggregate from
/// <c>ExecutionRecord</c>: <see cref="ExecutionId"/> is a plain value, not a foreign key, the
/// same pattern as <c>ExecutionRecord.PromptVersionId</c> (docs/execution-tracking.md) — a
/// collector reporting metrics never requires the execution to be loaded in the same transaction.
///
/// Immutable once created — each collection event produces its own row rather than updating a
/// shared one, since metrics "arrive asynchronously over time" from independent sources
/// (architecture.md §3) and a later collector run should never silently overwrite an earlier
/// one's numbers.
/// </summary>
public sealed class EngineeringMetrics : AggregateRoot
{
    public Guid Id { get; }
    public Guid ExecutionId { get; }
    public string CollectedBy { get; }
    public DateTimeOffset CollectedAt { get; }

    public bool? BuildSuccess { get; }
    public bool? TestSuccess { get; }
    public double? Coverage { get; }
    public int? SonarIssues { get; }
    public int? Warnings { get; }
    public int? CodeSmells { get; }
    public int? SecurityFindings { get; }
    public double? Duplication { get; }
    public double? CyclomaticComplexity { get; }
    public int? ReviewComments { get; }
    public int? ReviewIterations { get; }
    public double? MergeTimeMinutes { get; }
    public bool? RollbackNeeded { get; }
    public int? ManualEdits { get; }

    private EngineeringMetrics(
        Guid id,
        Guid executionId,
        string collectedBy,
        DateTimeOffset collectedAt,
        bool? buildSuccess,
        bool? testSuccess,
        double? coverage,
        int? sonarIssues,
        int? warnings,
        int? codeSmells,
        int? securityFindings,
        double? duplication,
        double? cyclomaticComplexity,
        int? reviewComments,
        int? reviewIterations,
        double? mergeTimeMinutes,
        bool? rollbackNeeded,
        int? manualEdits)
    {
        Id = id;
        ExecutionId = executionId;
        CollectedBy = collectedBy;
        CollectedAt = collectedAt;
        BuildSuccess = buildSuccess;
        TestSuccess = testSuccess;
        Coverage = coverage;
        SonarIssues = sonarIssues;
        Warnings = warnings;
        CodeSmells = codeSmells;
        SecurityFindings = securityFindings;
        Duplication = duplication;
        CyclomaticComplexity = cyclomaticComplexity;
        ReviewComments = reviewComments;
        ReviewIterations = reviewIterations;
        MergeTimeMinutes = mergeTimeMinutes;
        RollbackNeeded = rollbackNeeded;
        ManualEdits = manualEdits;
    }

    public static EngineeringMetrics Record(
        Guid executionId,
        string collectedBy,
        DateTimeOffset? collectedAt = null,
        bool? buildSuccess = null,
        bool? testSuccess = null,
        double? coverage = null,
        int? sonarIssues = null,
        int? warnings = null,
        int? codeSmells = null,
        int? securityFindings = null,
        double? duplication = null,
        double? cyclomaticComplexity = null,
        int? reviewComments = null,
        int? reviewIterations = null,
        double? mergeTimeMinutes = null,
        bool? rollbackNeeded = null,
        int? manualEdits = null)
    {
        if (executionId == Guid.Empty)
            throw new ArgumentException("executionId is required.", nameof(executionId));
        if (string.IsNullOrWhiteSpace(collectedBy))
            throw new ArgumentException("collectedBy is required.", nameof(collectedBy));

        var id = Guid.NewGuid();
        var timestamp = collectedAt ?? DateTimeOffset.UtcNow;

        var metrics = new EngineeringMetrics(
            id, executionId, collectedBy, timestamp,
            buildSuccess, testSuccess, coverage, sonarIssues, warnings, codeSmells, securityFindings,
            duplication, cyclomaticComplexity, reviewComments, reviewIterations, mergeTimeMinutes,
            rollbackNeeded, manualEdits);

        metrics.AddDomainEvent(new MetricsCollected(id, executionId, collectedBy, timestamp));
        return metrics;
    }

    /// <summary>Reconstructs a persisted metrics row (e.g. by a repository) — no domain event.</summary>
    public static EngineeringMetrics Rehydrate(
        Guid id,
        Guid executionId,
        string collectedBy,
        DateTimeOffset collectedAt,
        bool? buildSuccess,
        bool? testSuccess,
        double? coverage,
        int? sonarIssues,
        int? warnings,
        int? codeSmells,
        int? securityFindings,
        double? duplication,
        double? cyclomaticComplexity,
        int? reviewComments,
        int? reviewIterations,
        double? mergeTimeMinutes,
        bool? rollbackNeeded,
        int? manualEdits) => new(
            id, executionId, collectedBy, collectedAt,
            buildSuccess, testSuccess, coverage, sonarIssues, warnings, codeSmells, securityFindings,
            duplication, cyclomaticComplexity, reviewComments, reviewIterations, mergeTimeMinutes,
            rollbackNeeded, manualEdits);
}
