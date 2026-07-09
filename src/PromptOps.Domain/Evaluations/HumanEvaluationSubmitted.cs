namespace PromptOps.Domain.Evaluations;

/// <summary>Raised when a <see cref="HumanEvaluation"/> is submitted (ADR-0008).</summary>
public sealed record HumanEvaluationSubmitted(Guid EvaluationId, Guid ExecutionId, string EvaluatorId, DateTimeOffset Timestamp) : IDomainEvent;
