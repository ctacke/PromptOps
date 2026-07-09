namespace PromptOps.Domain.Metrics;

/// <summary>Raised when an <see cref="EngineeringMetrics"/> row is recorded (ADR-0008).</summary>
public sealed record MetricsCollected(Guid MetricsId, Guid ExecutionId, string CollectedBy, DateTimeOffset CollectedAt) : IDomainEvent;
