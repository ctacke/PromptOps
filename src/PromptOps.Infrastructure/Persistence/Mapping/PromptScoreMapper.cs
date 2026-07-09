using PromptOps.Domain.Scoring;
using PromptOps.Infrastructure.Persistence.Records;

namespace PromptOps.Infrastructure.Persistence.Mapping;

internal static class PromptScoreMapper
{
    public static PromptScoreEntity ToNewEntity(PromptScore score) => new()
    {
        Id = score.Id,
        PromptVersionId = score.PromptVersionId,
        ScoringConfigId = score.ScoringConfigId,
        ComputedAt = score.ComputedAt,
        OverallScore = score.OverallScore,
        ComponentScores = new Dictionary<string, double>(score.ComponentScores),
        SampleSize = score.SampleSize
    };

    public static PromptScore ToDomain(PromptScoreEntity entity) => PromptScore.Rehydrate(
        entity.Id,
        entity.PromptVersionId,
        entity.ScoringConfigId,
        entity.ComputedAt,
        entity.OverallScore,
        entity.ComponentScores,
        entity.SampleSize);
}
