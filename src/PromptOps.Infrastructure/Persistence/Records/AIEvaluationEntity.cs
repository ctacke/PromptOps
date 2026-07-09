namespace PromptOps.Infrastructure.Persistence.Records;

/// <summary>
/// EF Core persistence shape for <see cref="PromptOps.Domain.Evaluations.AIEvaluation"/>.
/// <see cref="ExecutionId"/> is deliberately not a foreign key — see
/// <see cref="Configurations.AIEvaluationEntityConfiguration"/>.
/// </summary>
public sealed class AIEvaluationEntity
{
    public Guid Id { get; set; }
    public Guid ExecutionId { get; set; }
    public string JudgeProviderId { get; set; } = string.Empty;
    public string? JudgeModel { get; set; }
    public bool? SatisfiesAcceptanceCriteria { get; set; }
    public List<string> AdrViolations { get; set; } = [];
    public List<string> IgnoredRequirements { get; set; } = [];
    public string? UnnecessaryComplexityNotes { get; set; }
    public List<string> SuggestedPromptImprovements { get; set; } = [];
    public string RawResponse { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
}
