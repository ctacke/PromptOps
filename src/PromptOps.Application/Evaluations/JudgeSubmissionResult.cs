using PromptOps.Domain.Evaluations;

namespace PromptOps.Application.Evaluations;

/// <summary>
/// Outcome of <see cref="DelegatedAIEvaluationService.SubmitAsync"/>: either the answer parsed and
/// was recorded, or it didn't and the client should try again with <see cref="RetryPrompt"/> — the
/// same tolerant retry the autonomous judge path runs internally, surfaced here as a result instead
/// of a hidden loop since each attempt is now a separate MCP tool call.
/// </summary>
public sealed record JudgeSubmissionResult
{
    public AIEvaluation? Evaluation { get; private init; }
    public string? RetryPrompt { get; private init; }

    public bool Succeeded => Evaluation is not null;

    public static JudgeSubmissionResult Recorded(AIEvaluation evaluation) => new() { Evaluation = evaluation };
    public static JudgeSubmissionResult RetryNeeded(string retryPrompt) => new() { RetryPrompt = retryPrompt };
}
