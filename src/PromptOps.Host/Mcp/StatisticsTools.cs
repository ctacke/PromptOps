using System.ComponentModel;
using ModelContextProtocol.Server;
using PromptOps.Application.Statistics;

namespace PromptOps.Host.Mcp;

/// <summary>A bird's-eye view of the shared database over MCP — reachable without curl, same rationale as every other tool class here.</summary>
[McpServerToolType]
public sealed class StatisticsTools(StatisticsService service)
{
    [McpServerTool(Name = "get_statistics")]
    [Description("Gets aggregate statistics for the whole PromptOps database: prompt/version counts by status, execution counts by status and repository, scoring summary (count + average), and human/AI evaluation counts.")]
    public async Task<object> GetStatistics(CancellationToken cancellationToken = default)
    {
        var statistics = await service.GetAsync(cancellationToken);

        return new
        {
            Prompts = new
            {
                statistics.Prompts.PromptCount,
                statistics.Prompts.VersionCount,
                statistics.Prompts.VersionCountByStatus
            },
            Executions = new
            {
                statistics.Executions.TotalCount,
                statistics.Executions.CountByStatus,
                statistics.Executions.CountByRepository
            },
            Scores = new
            {
                statistics.Scores.Count,
                statistics.Scores.AverageOverallScore
            },
            statistics.HumanEvaluationCount,
            statistics.AIEvaluationCount
        };
    }
}
