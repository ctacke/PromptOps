namespace PromptOps.Infrastructure.Persistence.Records;

/// <summary>
/// EF Core persistence shape for <see cref="PromptOps.Domain.Scoring.PromptScore"/>.
/// <see cref="PromptVersionId"/> and <see cref="ScoringConfigId"/> are deliberately not foreign
/// keys — see <see cref="Configurations.PromptScoreEntityConfiguration"/>.
/// </summary>
public sealed class PromptScoreEntity
{
    public Guid Id { get; set; }
    public Guid PromptVersionId { get; set; }
    public Guid ScoringConfigId { get; set; }
    public DateTimeOffset ComputedAt { get; set; }
    public double OverallScore { get; set; }
    public Dictionary<string, double> ComponentScores { get; set; } = [];
    public int SampleSize { get; set; }
}
