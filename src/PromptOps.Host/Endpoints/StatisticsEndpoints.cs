using PromptOps.Application.Executions;
using PromptOps.Application.Prompts;
using PromptOps.Application.Scoring;
using PromptOps.Application.Statistics;

namespace PromptOps.Host.Endpoints;

/// <summary>A single bird's-eye view of the shared database — prompt/version, execution, scoring, and evaluation counts, all computed in SQL.</summary>
public static class StatisticsEndpoints
{
    public static void MapStatisticsEndpoints(this WebApplication app)
    {
        app.MapGet("/statistics", async (StatisticsService service, CancellationToken cancellationToken) =>
        {
            var statistics = await service.GetAsync(cancellationToken);
            return Results.Ok(SystemStatisticsResponse.From(statistics));
        });
    }
}

internal sealed record PromptStatisticsResponse(int PromptCount, int VersionCount, IReadOnlyDictionary<string, int> VersionCountByStatus)
{
    public static PromptStatisticsResponse From(PromptStatistics stats) => new(stats.PromptCount, stats.VersionCount, stats.VersionCountByStatus);
}

internal sealed record ExecutionStatisticsResponse(int TotalCount, IReadOnlyDictionary<string, int> CountByStatus, IReadOnlyDictionary<string, int> CountByRepository)
{
    public static ExecutionStatisticsResponse From(ExecutionStatistics stats) => new(stats.TotalCount, stats.CountByStatus, stats.CountByRepository);
}

internal sealed record ScoreStatisticsResponse(int Count, double? AverageOverallScore)
{
    public static ScoreStatisticsResponse From(ScoreStatistics stats) => new(stats.Count, stats.AverageOverallScore);
}

internal sealed record SystemStatisticsResponse(
    PromptStatisticsResponse Prompts, ExecutionStatisticsResponse Executions, ScoreStatisticsResponse Scores,
    int HumanEvaluationCount, int AIEvaluationCount)
{
    public static SystemStatisticsResponse From(SystemStatistics statistics) => new(
        PromptStatisticsResponse.From(statistics.Prompts),
        ExecutionStatisticsResponse.From(statistics.Executions),
        ScoreStatisticsResponse.From(statistics.Scores),
        statistics.HumanEvaluationCount,
        statistics.AIEvaluationCount);
}
