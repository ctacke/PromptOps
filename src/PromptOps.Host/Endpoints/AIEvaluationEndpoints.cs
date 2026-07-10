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

        // Client-side automatic evaluation (Phase 13): thin wrappers around DelegatedAIEvaluationService
        // (ADR-0010/Phase 12), reachable from the per-repo plugin's SessionEnd hook — a plain Node.js
        // script that fetches this loopback API rather than an MCP client, so it can't call
        // prepare_ai_evaluation/submit_ai_evaluation_result directly (docs/ai-evaluation.md).
        group.MapPost("/prepare", async (Guid id, DelegatedAIEvaluationService service, CancellationToken cancellationToken) =>
        {
            try
            {
                var prepared = await service.PrepareAsync(id, cancellationToken);
                return Results.Ok(new PrepareAIEvaluationResponse(prepared.CorrelationId, prepared.Prompt));
            }
            catch (ExecutionNotFoundException)
            {
                return Results.NotFound();
            }
        });

        group.MapPost("/submit", async (Guid id, SubmitAIEvaluationRequest request, DelegatedAIEvaluationService service, CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await service.SubmitAsync(request.CorrelationId, request.Response, cancellationToken);

                return result.Succeeded
                    ? Results.Ok(SubmitAIEvaluationResponse.Recorded(result.Evaluation!))
                    : Results.Ok(SubmitAIEvaluationResponse.RetryNeeded(request.CorrelationId, result.RetryPrompt!));
            }
            catch (PendingEvaluationNotFoundException)
            {
                return Results.NotFound();
            }
            catch (AIJudgeResponseInvalidException ex)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status502BadGateway,
                    title: "AI judge did not return a valid evaluation",
                    detail: ex.Message);
            }
        });
    }
}

internal sealed record RunAIEvaluationRequest(Dictionary<string, string>? Parameters);

internal sealed record PrepareAIEvaluationResponse(Guid CorrelationId, string Prompt);

internal sealed record SubmitAIEvaluationRequest(Guid CorrelationId, string Response);

internal sealed record SubmitAIEvaluationResponse(string Status, Guid? CorrelationId, string? Prompt, AIEvaluationResponse? Evaluation)
{
    public static SubmitAIEvaluationResponse Recorded(AIEvaluation evaluation) => new("recorded", null, null, AIEvaluationResponse.From(evaluation));
    public static SubmitAIEvaluationResponse RetryNeeded(Guid correlationId, string prompt) => new("retry_needed", correlationId, prompt, null);
}

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
