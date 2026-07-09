namespace PromptOps.Domain.Executions;

/// <summary>Raised when an <see cref="ExecutionRecord"/> is finished — the point at which the full record (including output/metrics) exists.</summary>
public sealed record ExecutionRecorded(Guid ExecutionId, Guid PromptVersionId, string Repository, DateTimeOffset Timestamp) : IDomainEvent;
