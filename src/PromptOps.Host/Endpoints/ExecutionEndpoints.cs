using PromptOps.Application.Executions;
using PromptOps.Domain.Executions;

namespace PromptOps.Host.Endpoints;

/// <summary>
/// The loopback ingestion API (ADR-0006): the contract between per-repo Claude Code hooks and the
/// daemon. Hooks push git-derived facts they alone can compute (ADR-0005 §9) — the daemon never
/// reads a repo's filesystem itself. Real Docker/localhost-only binding arrives in Phase 4a; this
/// phase builds the endpoints and proves the recording pipeline.
/// </summary>
public static class ExecutionEndpoints
{
    public static void MapExecutionEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/executions");

        group.MapPost("/start", async (StartExecutionRequest request, ExecutionService service, CancellationToken cancellationToken) =>
        {
            var context = new DevelopmentContext
            {
                Repository = request.Repository,
                Branch = request.Branch,
                Commit = request.Commit,
                TaskId = request.TaskId,
                ReferencedDocuments = request.ReferencedDocuments ?? [],
                ReferencedADRs = request.ReferencedADRs ?? [],
                AcceptanceCriteria = request.AcceptanceCriteria ?? [],
                Languages = request.Languages ?? []
            };

            var execution = await service.StartExecutionAsync(
                request.PromptVersionId, request.DeveloperId, context, request.Inputs, cancellationToken);

            return Results.Ok(new StartExecutionResponse(execution.Id));
        });

        // Phase 15: the attributed counterpart to /start. The hook's UserPromptSubmit calls this with
        // the user's first prompt so the daemon can classify it, then either attribute the execution
        // to an existing prompt, capture a new one for a novel development activity, or leave it
        // untracked for non-development chatter — resolving the PromptVersion *before* opening the
        // (immutable-once-created) ExecutionRecord.
        group.MapPost("/start-attributed", async (StartAttributedRequest request, ExecutionAttributionService service, CancellationToken cancellationToken) =>
        {
            var context = new DevelopmentContext
            {
                Repository = request.Repository,
                Branch = request.Branch,
                Commit = request.Commit,
                Languages = request.Languages ?? []
            };

            var result = await service.StartAttributedAsync(
                taskDescription: request.Prompt,
                rawPrompt: request.Prompt,
                developerId: request.DeveloperId,
                context: context,
                parameters: request.Parameters,
                cancellationToken: cancellationToken);

            return Results.Ok(new StartAttributedResponse(
                result.ExecutionId,
                result.PromptVersionId,
                result.Attribution.ToString().ToLowerInvariant(),
                result.Content,
                result.Rationale));
        });

        group.MapPost("/{id:guid}/tool-usage", async (Guid id, RecordToolUsageRequest request, ExecutionService service, CancellationToken cancellationToken) =>
        {
            try
            {
                await service.RecordToolUsageAsync(id, request.Name, request.Count, TimeSpan.FromMilliseconds(request.DurationMs), cancellationToken);
                return Results.NoContent();
            }
            catch (ExecutionNotFoundException)
            {
                return Results.NotFound();
            }
        });

        group.MapPost("/{id:guid}/finish", async (Guid id, FinishExecutionRequest request, ExecutionService service, CancellationToken cancellationToken) =>
        {
            try
            {
                var execution = await service.FinishExecutionAsync(
                    id,
                    request.Output,
                    TimeSpan.FromMilliseconds(request.ExecutionTimeMs),
                    request.AiProviderId,
                    request.Model,
                    request.ModelParameters,
                    request.FilesChanged ?? [],
                    request.LinesAdded,
                    request.LinesDeleted,
                    cancellationToken);

                return Results.Ok(ExecutionResponse.From(execution));
            }
            catch (ExecutionNotFoundException)
            {
                return Results.NotFound();
            }
        });

        group.MapGet("/{id:guid}", async (Guid id, ExecutionService service, CancellationToken cancellationToken) =>
        {
            var execution = await service.GetByIdAsync(id, cancellationToken);
            return execution is null ? Results.NotFound() : Results.Ok(ExecutionResponse.From(execution));
        });
    }
}

internal sealed record StartExecutionRequest(
    Guid PromptVersionId,
    string DeveloperId,
    string Repository,
    string? Branch,
    string? Commit,
    string? TaskId,
    List<string>? ReferencedDocuments,
    List<string>? ReferencedADRs,
    List<string>? AcceptanceCriteria,
    List<string>? Languages,
    Dictionary<string, string>? Inputs);

internal sealed record StartExecutionResponse(Guid ExecutionId);

internal sealed record StartAttributedRequest(
    string Prompt,
    string DeveloperId,
    string Repository,
    string? Branch,
    string? Commit,
    List<string>? Languages,
    Dictionary<string, string>? Parameters);

internal sealed record StartAttributedResponse(
    Guid ExecutionId,
    Guid PromptVersionId,
    string Attribution,
    string? Content,
    string? Rationale);

internal sealed record RecordToolUsageRequest(string Name, int Count, long DurationMs);

internal sealed record FinishExecutionRequest(
    string? Output,
    long ExecutionTimeMs,
    string? AiProviderId,
    string? Model,
    string? ModelParameters,
    List<string>? FilesChanged,
    int LinesAdded,
    int LinesDeleted);

internal sealed record ExecutionResponse(
    Guid Id,
    Guid PromptVersionId,
    string DeveloperId,
    string Status,
    string? Output,
    IReadOnlyList<string> FilesChanged,
    int LinesAdded,
    int LinesDeleted,
    int ToolUsageCount)
{
    public static ExecutionResponse From(ExecutionRecord execution) => new(
        execution.Id,
        execution.PromptVersionId,
        execution.DeveloperId,
        execution.Status.ToString(),
        execution.Output,
        execution.FilesChanged,
        execution.LinesAdded,
        execution.LinesDeleted,
        execution.ToolUsage.Count);
}
