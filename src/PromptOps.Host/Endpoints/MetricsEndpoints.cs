using PromptOps.Application.Executions;
using PromptOps.Application.Metrics;
using PromptOps.Domain.Metrics;

namespace PromptOps.Host.Endpoints;

/// <summary>
/// Metrics collection (Phase 5): one generic trigger, fanned out to every registered
/// <see cref="PromptOps.Application.Providers.IMetricCollector"/> by <see cref="MetricsCollectionService"/>.
/// Which collectors actually produce something depends entirely on what's registered and what
/// <c>parameters</c> contains — e.g. a Sonar collector self-serves over the network and mostly
/// ignores <c>parameters</c>, while a build-result collector needs <c>parameters["trx"]</c> and/or
/// <c>parameters["cobertura"]</c> pushed to it (the daemon has no filesystem access to CI
/// artifacts — same ADR-0005 §9 rationale as <see cref="ExecutionEndpoints"/>).
/// </summary>
public static class MetricsEndpoints
{
    public static void MapMetricsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/executions/{id:guid}/metrics");

        group.MapPost("/collect", async (Guid id, CollectMetricsRequest? request, MetricsCollectionService service, CancellationToken cancellationToken) =>
        {
            try
            {
                var collected = await service.CollectAsync(id, request?.Parameters ?? new Dictionary<string, string>(), cancellationToken);
                return Results.Ok(collected.Select(EngineeringMetricsResponse.From));
            }
            catch (ExecutionNotFoundException)
            {
                return Results.NotFound();
            }
        });

        group.MapGet("/", async (Guid id, MetricsCollectionService service, CancellationToken cancellationToken) =>
        {
            var metrics = await service.GetByExecutionIdAsync(id, cancellationToken);
            return Results.Ok(metrics.Select(EngineeringMetricsResponse.From));
        });
    }
}

internal sealed record CollectMetricsRequest(Dictionary<string, string>? Parameters);

internal sealed record EngineeringMetricsResponse(
    Guid Id,
    Guid ExecutionId,
    string CollectedBy,
    DateTimeOffset CollectedAt,
    bool? BuildSuccess,
    bool? TestSuccess,
    double? Coverage,
    int? SonarIssues,
    int? Warnings,
    int? CodeSmells,
    int? SecurityFindings,
    double? Duplication,
    double? CyclomaticComplexity,
    int? ReviewComments,
    int? ReviewIterations,
    double? MergeTimeMinutes,
    bool? RollbackNeeded,
    int? ManualEdits)
{
    public static EngineeringMetricsResponse From(EngineeringMetrics metrics) => new(
        metrics.Id,
        metrics.ExecutionId,
        metrics.CollectedBy,
        metrics.CollectedAt,
        metrics.BuildSuccess,
        metrics.TestSuccess,
        metrics.Coverage,
        metrics.SonarIssues,
        metrics.Warnings,
        metrics.CodeSmells,
        metrics.SecurityFindings,
        metrics.Duplication,
        metrics.CyclomaticComplexity,
        metrics.ReviewComments,
        metrics.ReviewIterations,
        metrics.MergeTimeMinutes,
        metrics.RollbackNeeded,
        metrics.ManualEdits);
}
