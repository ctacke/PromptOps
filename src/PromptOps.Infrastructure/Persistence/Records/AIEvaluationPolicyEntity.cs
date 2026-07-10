namespace PromptOps.Infrastructure.Persistence.Records;

/// <summary>EF Core persistence shape for <see cref="PromptOps.Domain.Evaluations.AIEvaluationPolicy"/> — a single row, same singleton-settings pattern as <c>PromotionPolicyEntity</c>.</summary>
public sealed class AIEvaluationPolicyEntity
{
    public Guid Id { get; set; }
    public bool AutoEvaluateOnFinish { get; set; }

    /// <summary>Enum name (e.g. "Daemon", "ClientHook") — same string-column convention as <c>ExecutionRecordEntity.Status</c>.</summary>
    public string Mechanism { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
}
