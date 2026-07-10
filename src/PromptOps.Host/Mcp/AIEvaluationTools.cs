using System.ComponentModel;
using ModelContextProtocol.Server;
using PromptOps.Application.Evaluations;

namespace PromptOps.Host.Mcp;

/// <summary>
/// The manual escape hatch for AI-judge evaluation, plus policy management — reachable without
/// curl whether or not <c>AIEvaluationPolicy.AutoEvaluateOnFinish</c> is on, and usable to force a
/// re-run (each call is additive, same "immutable, append-only" pattern as <c>PromptScore</c>).
/// </summary>
[McpServerToolType]
public sealed class AIEvaluationTools(AIEvaluationService evaluationService, AIEvaluationPolicyService policyService)
{
    [McpServerTool(Name = "run_ai_evaluation")]
    [Description("Runs the AI judge against a PromptOps execution and returns its verdict (acceptance criteria, ADR violations, suggested prompt improvements). Safe to call again to force a re-run.")]
    public async Task<object> RunAIEvaluation(
        [Description("The execution id to evaluate.")] Guid executionId,
        [Description("Optional provider-specific parameters.")] Dictionary<string, string>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        var evaluation = await evaluationService.EvaluateAsync(executionId, parameters, cancellationToken);

        return new
        {
            evaluation.Id,
            evaluation.ExecutionId,
            evaluation.JudgeProviderId,
            evaluation.JudgeModel,
            evaluation.SatisfiesAcceptanceCriteria,
            evaluation.AdrViolations,
            evaluation.IgnoredRequirements,
            evaluation.UnnecessaryComplexityNotes,
            evaluation.SuggestedPromptImprovements,
            evaluation.Timestamp
        };
    }

    [McpServerTool(Name = "get_ai_evaluation_policy")]
    [Description("Gets whether AI evaluation runs automatically when an execution finishes.")]
    public async Task<object> GetAIEvaluationPolicy(CancellationToken cancellationToken = default)
    {
        var policy = await policyService.GetOrCreateDefaultAsync(cancellationToken);
        return new { policy.Id, policy.AutoEvaluateOnFinish, policy.UpdatedAt };
    }

    [McpServerTool(Name = "update_ai_evaluation_policy")]
    [Description("Turns automatic AI evaluation on execution finish on or off.")]
    public async Task<object> UpdateAIEvaluationPolicy(
        [Description("Whether the daemon should automatically run the AI judge when an execution finishes.")] bool autoEvaluateOnFinish,
        CancellationToken cancellationToken = default)
    {
        var policy = await policyService.UpdateAsync(autoEvaluateOnFinish, cancellationToken);
        return new { policy.Id, policy.AutoEvaluateOnFinish, policy.UpdatedAt };
    }
}
