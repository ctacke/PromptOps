using PromptOps.Domain.Evaluations;
using PromptOps.Infrastructure.Persistence.Records;

namespace PromptOps.Infrastructure.Persistence.Mapping;

internal static class HumanEvaluationMapper
{
    public static HumanEvaluationEntity ToNewEntity(HumanEvaluation evaluation) => new()
    {
        Id = evaluation.Id,
        ExecutionId = evaluation.ExecutionId,
        EvaluatorId = evaluation.EvaluatorId,
        Correctness = evaluation.Correctness,
        Helpfulness = evaluation.Helpfulness,
        Architecture = evaluation.Architecture,
        Readability = evaluation.Readability,
        Completeness = evaluation.Completeness,
        Hallucinations = evaluation.Hallucinations,
        Confidence = evaluation.Confidence,
        OverallSatisfaction = evaluation.OverallSatisfaction,
        Notes = evaluation.Notes,
        Timestamp = evaluation.Timestamp
    };

    public static HumanEvaluation ToDomain(HumanEvaluationEntity entity) => HumanEvaluation.Rehydrate(
        entity.Id,
        entity.ExecutionId,
        entity.EvaluatorId,
        entity.Correctness,
        entity.Helpfulness,
        entity.Architecture,
        entity.Readability,
        entity.Completeness,
        entity.Hallucinations,
        entity.Confidence,
        entity.OverallSatisfaction,
        entity.Notes,
        entity.Timestamp);
}
