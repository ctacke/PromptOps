namespace PromptOps.Domain.Evaluations;

/// <summary>Raised when an <see cref="AIEvaluation"/> is recorded (ADR-0008).</summary>
public sealed record AIEvaluationRecorded(Guid EvaluationId, Guid ExecutionId, string JudgeProviderId, DateTimeOffset Timestamp) : IDomainEvent;
