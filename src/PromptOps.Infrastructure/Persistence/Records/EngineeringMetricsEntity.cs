namespace PromptOps.Infrastructure.Persistence.Records;

/// <summary>
/// EF Core persistence shape for <see cref="PromptOps.Domain.Metrics.EngineeringMetrics"/>.
/// <see cref="ExecutionId"/> is deliberately not a foreign key — see
/// <see cref="Configurations.EngineeringMetricsEntityConfiguration"/>.
/// </summary>
public sealed class EngineeringMetricsEntity
{
    public Guid Id { get; set; }
    public Guid ExecutionId { get; set; }
    public string CollectedBy { get; set; } = string.Empty;
    public DateTimeOffset CollectedAt { get; set; }

    public bool? BuildSuccess { get; set; }
    public bool? TestSuccess { get; set; }
    public double? Coverage { get; set; }
    public int? SonarIssues { get; set; }
    public int? Warnings { get; set; }
    public int? CodeSmells { get; set; }
    public int? SecurityFindings { get; set; }
    public double? Duplication { get; set; }
    public double? CyclomaticComplexity { get; set; }
    public int? ReviewComments { get; set; }
    public int? ReviewIterations { get; set; }
    public double? MergeTimeMinutes { get; set; }
    public bool? RollbackNeeded { get; set; }
    public int? ManualEdits { get; set; }
}
