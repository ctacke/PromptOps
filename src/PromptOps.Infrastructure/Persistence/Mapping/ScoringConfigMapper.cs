using PromptOps.Domain.Scoring;
using PromptOps.Infrastructure.Persistence.Records;

namespace PromptOps.Infrastructure.Persistence.Mapping;

internal static class ScoringConfigMapper
{
    public static ScoringConfigEntity ToNewEntity(ScoringConfig config) => new()
    {
        Id = config.Id,
        Name = config.Name,
        Version = config.Version,
        CreatedAt = config.CreatedAt,
        WeightHumanRating = config.Weights.HumanRating,
        WeightSonar = config.Weights.Sonar,
        WeightTests = config.Weights.Tests,
        WeightBuild = config.Weights.Build,
        WeightAcceptanceCriteria = config.Weights.AcceptanceCriteria,
        WeightManualFixes = config.Weights.ManualFixes,
        WeightReviewComments = config.Weights.ReviewComments,
        WeightRegressionBugs = config.Weights.RegressionBugs
    };

    public static ScoringConfig ToDomain(ScoringConfigEntity entity) => ScoringConfig.Rehydrate(
        entity.Id,
        entity.Name,
        entity.Version,
        new ScoringWeights
        {
            HumanRating = entity.WeightHumanRating,
            Sonar = entity.WeightSonar,
            Tests = entity.WeightTests,
            Build = entity.WeightBuild,
            AcceptanceCriteria = entity.WeightAcceptanceCriteria,
            ManualFixes = entity.WeightManualFixes,
            ReviewComments = entity.WeightReviewComments,
            RegressionBugs = entity.WeightRegressionBugs
        },
        entity.CreatedAt);
}
