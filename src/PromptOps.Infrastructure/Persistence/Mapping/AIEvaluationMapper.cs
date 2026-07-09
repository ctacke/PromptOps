using PromptOps.Domain.Evaluations;
using PromptOps.Infrastructure.Persistence.Records;

namespace PromptOps.Infrastructure.Persistence.Mapping;

internal static class AIEvaluationMapper
{
    public static AIEvaluationEntity ToNewEntity(AIEvaluation evaluation) => new()
    {
        Id = evaluation.Id,
        ExecutionId = evaluation.ExecutionId,
        JudgeProviderId = evaluation.JudgeProviderId,
        JudgeModel = evaluation.JudgeModel,
        SatisfiesAcceptanceCriteria = evaluation.SatisfiesAcceptanceCriteria,
        AdrViolations = evaluation.AdrViolations.ToList(),
        IgnoredRequirements = evaluation.IgnoredRequirements.ToList(),
        UnnecessaryComplexityNotes = evaluation.UnnecessaryComplexityNotes,
        SuggestedPromptImprovements = evaluation.SuggestedPromptImprovements.ToList(),
        RawResponse = evaluation.RawResponse,
        Timestamp = evaluation.Timestamp
    };

    public static AIEvaluation ToDomain(AIEvaluationEntity entity) => AIEvaluation.Rehydrate(
        entity.Id,
        entity.ExecutionId,
        entity.JudgeProviderId,
        entity.JudgeModel,
        entity.SatisfiesAcceptanceCriteria,
        entity.AdrViolations,
        entity.IgnoredRequirements,
        entity.UnnecessaryComplexityNotes,
        entity.SuggestedPromptImprovements,
        entity.RawResponse,
        entity.Timestamp);
}
