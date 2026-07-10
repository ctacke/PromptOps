using System.ComponentModel;
using ModelContextProtocol.Server;
using PromptOps.Application.Evaluations;

namespace PromptOps.Host.Mcp;

/// <summary>
/// Client-delegated AI evaluation (ADR-0010/Phase 12) — the replacement for MCP's deprecated
/// <c>sampling/createMessage</c>: instead of the daemon calling a model itself
/// (<see cref="AIEvaluationTools.RunAIEvaluation"/>), these two tools hand the judge prompt back to
/// whichever MCP client is in the conversation to answer with its own model/session, then accept
/// that answer back. Provider-agnostic by construction — any MCP client that can call a tool,
/// reason over the returned text, and call a tool back satisfies the contract.
/// </summary>
[McpServerToolType]
public sealed class DelegatedAIEvaluationTools(DelegatedAIEvaluationService delegatedService)
{
    [McpServerTool(Name = "prepare_ai_evaluation")]
    [Description("Starts a client-delegated AI evaluation. Returns a judge prompt for YOU (the calling assistant) to answer yourself, using your own current reasoning/model — do not call any other tool or backend to answer it. Then call submit_ai_evaluation_result with your answer and the returned correlationId.")]
    public async Task<object> PrepareAIEvaluation(
        [Description("The execution id to evaluate.")] Guid executionId,
        CancellationToken cancellationToken = default)
    {
        var prepared = await delegatedService.PrepareAsync(executionId, cancellationToken);
        return new { prepared.CorrelationId, prepared.Prompt };
    }

    [McpServerTool(Name = "submit_ai_evaluation_result")]
    [Description("Submits YOUR answer to a prompt previously returned by prepare_ai_evaluation. Returns the persisted evaluation, or asks you to retry with a corrected answer if your response didn't match the required JSON schema.")]
    public async Task<object> SubmitAIEvaluationResult(
        [Description("The correlationId returned by prepare_ai_evaluation.")] Guid correlationId,
        [Description("Your answer to the judge prompt.")] string response,
        CancellationToken cancellationToken = default)
    {
        var result = await delegatedService.SubmitAsync(correlationId, response, cancellationToken);

        if (result.Succeeded)
        {
            var evaluation = result.Evaluation!;
            return new
            {
                status = "recorded",
                evaluation.Id,
                evaluation.ExecutionId,
                evaluation.JudgeProviderId,
                evaluation.SatisfiesAcceptanceCriteria,
                evaluation.AdrViolations,
                evaluation.IgnoredRequirements,
                evaluation.UnnecessaryComplexityNotes,
                evaluation.SuggestedPromptImprovements,
                evaluation.Timestamp
            };
        }

        return new { status = "retry_needed", correlationId, prompt = result.RetryPrompt };
    }
}
