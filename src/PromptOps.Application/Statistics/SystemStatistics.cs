using PromptOps.Application.Executions;
using PromptOps.Application.Prompts;
using PromptOps.Application.Scoring;

namespace PromptOps.Application.Statistics;

/// <summary>A single bird's-eye view of the shared database, composed by <see cref="StatisticsService"/> from each aggregate's own repository — every field here is computed in SQL, never by loading full aggregates into memory.</summary>
public sealed record SystemStatistics(
    PromptStatistics Prompts,
    ExecutionStatistics Executions,
    ScoreStatistics Scores,
    int HumanEvaluationCount,
    int AIEvaluationCount);
