namespace PromptOps.Domain.Scoring;

/// <summary>Raised when a <see cref="PromptScore"/> is computed (ADR-0008 names this event explicitly).</summary>
public sealed record ScoreComputed(Guid PromptScoreId, Guid PromptVersionId, Guid ScoringConfigId, double OverallScore, DateTimeOffset ComputedAt) : IDomainEvent;
