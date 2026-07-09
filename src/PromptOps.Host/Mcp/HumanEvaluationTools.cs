using System.ComponentModel;
using ModelContextProtocol.Server;
using PromptOps.Application.Evaluations;

namespace PromptOps.Host.Mcp;

/// <summary>
/// Lets the agent submit and reference human evaluations mid-session (ADR-0006: "submit a
/// rating" is one of the canonical examples of why MCP tools exist alongside the ingestion API).
/// The phase 6 acceptance bar only requires retrieval over MCP; submission is included too since
/// it's the same <see cref="HumanEvaluationService"/> already built for the ingestion endpoint,
/// and letting the agent call it directly is cleaner than instructing it to shell out to curl.
/// Instance (not static) tool methods — the MCP server resolves this type per call via DI, so
/// <see cref="HumanEvaluationService"/> can be constructor-injected like anywhere else.
/// </summary>
[McpServerToolType]
public sealed class HumanEvaluationTools(HumanEvaluationService service)
{
    [McpServerTool(Name = "submit_human_evaluation")]
    [Description("Submits a human rating (1-5 scale unless noted) for a PromptOps execution.")]
    public async Task<object> SubmitHumanEvaluation(
        [Description("The execution id being rated.")] Guid executionId,
        [Description("Identifier for who's rating (e.g. developer email/username).")] string evaluatorId,
        [Description("1-5: did the output correctly solve the task?")] int correctness,
        [Description("1-5: how helpful was the output?")] int helpfulness,
        [Description("1-5: architectural quality of the output.")] int architecture,
        [Description("1-5: readability of the output.")] int readability,
        [Description("1-5: how complete was the output relative to what was asked?")] int completeness,
        [Description("Did the output contain hallucinated facts/APIs/behavior?")] bool hallucinations,
        [Description("1-5: the evaluator's own confidence in this rating.")] int confidence,
        [Description("1-5: overall satisfaction with the output.")] int overallSatisfaction,
        [Description("Optional free-text notes.")] string? notes = null,
        CancellationToken cancellationToken = default)
    {
        var evaluation = await service.SubmitAsync(
            executionId, evaluatorId, correctness, helpfulness, architecture, readability,
            completeness, hallucinations, confidence, overallSatisfaction, notes, cancellationToken);

        return new { evaluation.Id, evaluation.ExecutionId, evaluation.Timestamp };
    }

    [McpServerTool(Name = "get_human_evaluations")]
    [Description("Retrieves all human evaluations submitted for a PromptOps execution.")]
    public async Task<object> GetHumanEvaluations(
        [Description("The execution id to look up.")] Guid executionId,
        CancellationToken cancellationToken = default)
    {
        var evaluations = await service.GetByExecutionIdAsync(executionId, cancellationToken);

        return evaluations.Select(e => new
        {
            e.Id,
            e.EvaluatorId,
            e.Correctness,
            e.Helpfulness,
            e.Architecture,
            e.Readability,
            e.Completeness,
            e.Hallucinations,
            e.Confidence,
            e.OverallSatisfaction,
            e.Notes,
            e.Timestamp
        });
    }
}
