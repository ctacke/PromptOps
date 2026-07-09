using PromptOps.Application.Evaluations;
using PromptOps.Application.Executions;
using PromptOps.Domain.Evaluations;

namespace PromptOps.Host.Endpoints;

/// <summary>
/// Human evaluation (Phase 6): what <c>/promptops rate</c> submits to and what the daemon's MCP
/// tools (<see cref="Mcp.HumanEvaluationTools"/>) read from — the same underlying
/// <see cref="HumanEvaluationService"/>, just reached over the ingestion API instead of MCP.
/// </summary>
public static class EvaluationEndpoints
{
    public static void MapEvaluationEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/executions/{id:guid}/evaluations");

        group.MapPost("/", async (Guid id, SubmitEvaluationRequest request, HumanEvaluationService service, CancellationToken cancellationToken) =>
        {
            try
            {
                var evaluation = await service.SubmitAsync(
                    id, request.EvaluatorId, request.Correctness, request.Helpfulness, request.Architecture,
                    request.Readability, request.Completeness, request.Hallucinations, request.Confidence,
                    request.OverallSatisfaction, request.Notes, cancellationToken);

                return Results.Ok(HumanEvaluationResponse.From(evaluation));
            }
            catch (ExecutionNotFoundException)
            {
                return Results.NotFound();
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        group.MapGet("/", async (Guid id, HumanEvaluationService service, CancellationToken cancellationToken) =>
        {
            var evaluations = await service.GetByExecutionIdAsync(id, cancellationToken);
            return Results.Ok(evaluations.Select(HumanEvaluationResponse.From));
        });
    }
}

internal sealed record SubmitEvaluationRequest(
    string EvaluatorId,
    int Correctness,
    int Helpfulness,
    int Architecture,
    int Readability,
    int Completeness,
    bool Hallucinations,
    int Confidence,
    int OverallSatisfaction,
    string? Notes);

internal sealed record HumanEvaluationResponse(
    Guid Id,
    Guid ExecutionId,
    string EvaluatorId,
    int Correctness,
    int Helpfulness,
    int Architecture,
    int Readability,
    int Completeness,
    bool Hallucinations,
    int Confidence,
    int OverallSatisfaction,
    string? Notes,
    DateTimeOffset Timestamp)
{
    public static HumanEvaluationResponse From(HumanEvaluation evaluation) => new(
        evaluation.Id,
        evaluation.ExecutionId,
        evaluation.EvaluatorId,
        evaluation.Correctness,
        evaluation.Helpfulness,
        evaluation.Architecture,
        evaluation.Readability,
        evaluation.Completeness,
        evaluation.Hallucinations,
        evaluation.Confidence,
        evaluation.OverallSatisfaction,
        evaluation.Notes,
        evaluation.Timestamp);
}
