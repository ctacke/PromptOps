namespace PromptOps.Infrastructure.Persistence.Records;

/// <summary>
/// EF Core persistence shape for <see cref="PromptOps.Domain.Evaluations.HumanEvaluation"/>.
/// <see cref="ExecutionId"/> is deliberately not a foreign key — see
/// <see cref="Configurations.HumanEvaluationEntityConfiguration"/>.
/// </summary>
public sealed class HumanEvaluationEntity
{
    public Guid Id { get; set; }
    public Guid ExecutionId { get; set; }
    public string EvaluatorId { get; set; } = string.Empty;
    public int Correctness { get; set; }
    public int Helpfulness { get; set; }
    public int Architecture { get; set; }
    public int Readability { get; set; }
    public int Completeness { get; set; }
    public bool Hallucinations { get; set; }
    public int Confidence { get; set; }
    public int OverallSatisfaction { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset Timestamp { get; set; }
}
