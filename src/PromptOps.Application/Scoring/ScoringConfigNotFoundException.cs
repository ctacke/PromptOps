namespace PromptOps.Application.Scoring;

public sealed class ScoringConfigNotFoundException(Guid scoringConfigId)
    : Exception($"ScoringConfig '{scoringConfigId}' was not found.")
{
    public Guid ScoringConfigId { get; } = scoringConfigId;
}
