using PromptOps.Domain.Metrics;
using PromptOps.Infrastructure.Persistence.Records;

namespace PromptOps.Infrastructure.Persistence.Mapping;

internal static class EngineeringMetricsMapper
{
    public static EngineeringMetricsEntity ToNewEntity(EngineeringMetrics metrics) => new()
    {
        Id = metrics.Id,
        ExecutionId = metrics.ExecutionId,
        CollectedBy = metrics.CollectedBy,
        CollectedAt = metrics.CollectedAt,
        BuildSuccess = metrics.BuildSuccess,
        TestSuccess = metrics.TestSuccess,
        Coverage = metrics.Coverage,
        SonarIssues = metrics.SonarIssues,
        Warnings = metrics.Warnings,
        CodeSmells = metrics.CodeSmells,
        SecurityFindings = metrics.SecurityFindings,
        Duplication = metrics.Duplication,
        CyclomaticComplexity = metrics.CyclomaticComplexity,
        ReviewComments = metrics.ReviewComments,
        ReviewIterations = metrics.ReviewIterations,
        MergeTimeMinutes = metrics.MergeTimeMinutes,
        RollbackNeeded = metrics.RollbackNeeded,
        ManualEdits = metrics.ManualEdits
    };

    public static EngineeringMetrics ToDomain(EngineeringMetricsEntity entity) => EngineeringMetrics.Rehydrate(
        entity.Id,
        entity.ExecutionId,
        entity.CollectedBy,
        entity.CollectedAt,
        entity.BuildSuccess,
        entity.TestSuccess,
        entity.Coverage,
        entity.SonarIssues,
        entity.Warnings,
        entity.CodeSmells,
        entity.SecurityFindings,
        entity.Duplication,
        entity.CyclomaticComplexity,
        entity.ReviewComments,
        entity.ReviewIterations,
        entity.MergeTimeMinutes,
        entity.RollbackNeeded,
        entity.ManualEdits);
}
