using PromptOps.Application.Evaluations;
using PromptOps.Application.Executions;
using PromptOps.Domain.Evaluations;

namespace PromptOps.Host.Endpoints;

/// <summary>
/// AI evaluation pipeline (Phase 7): triggers <see cref="AIEvaluationService"/> to run the
/// configured judge (<c>IAIEvaluationProvider</c>) against an execution and persist the result,
/// stored separately from <see cref="EvaluationEndpoints"/>'s human evaluations by requirement.
/// </summary>
public static class AIEvaluationEndpoints
{
    public static void MapAIEvaluationEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/executions/{id:guid}/ai-evaluations");

        group.MapPost("/", async (Guid id, RunAIEvaluationRequest? request, AIEvaluationService service, CancellationToken cancellationToken) =>
        {
            try
            {
                var evaluation = await service.EvaluateAsync(id, request?.Parameters, cancellationToken);
                return Results.Ok(AIEvaluationResponse.From(evaluation));
            }
            catch (ExecutionNotFoundException)
            {
                return Results.NotFound();
            }
            catch (AIJudgeResponseInvalidException ex)
            {
                // The judge itself is the failure here, not the caller's request — 502, not 400/500.
                return Results.Problem(
                    statusCode: StatusCodes.Status502BadGateway,
                    title: "AI judge did not return a valid evaluation",
                    detail: ex.Message);
            }
        });

        group.MapGet("/", async (Guid id, AIEvaluationService service, CancellationToken cancellationToken) =>
        {
            var evaluations = await service.GetByExecutionIdAsync(id, cancellationToken);
            return Results.Ok(evaluations.Select(AIEvaluationResponse.From));
        });
    }
}

internal sealed record RunAIEvaluationRequest(Dictionary<string, string>? Parameters);

internal sealed record AIEvaluationResponse(
    Guid Id,
    Guid ExecutionId,
    string JudgeProviderId,
    string? JudgeModel,
    bool? SatisfiesAcceptanceCriteria,
    IReadOnlyList<string> AdrViolations,
    IReadOnlyList<string> IgnoredRequirements,
    string? UnnecessaryComplexityNotes,
    IReadOnlyList<string> SuggestedPromptImprovements,
    string RawResponse,
    DateTimeOffset Timestamp)
{
    public static AIEvaluationResponse From(AIEvaluation evaluation) => new(
        evaluation.Id,
        evaluation.ExecutionId,
        evaluation.JudgeProviderId,
        evaluation.JudgeModel,
        evaluation.SatisfiesAcceptanceCriteria,
        evaluation.AdrViolations,
        evaluation.IgnoredRequirements,
        evaluation.UnnecessaryComplexityNotes,
        evaluation.SuggestedPromptImprovements,
        evaluation.RawResponse,
        evaluation.Timestamp);
}
