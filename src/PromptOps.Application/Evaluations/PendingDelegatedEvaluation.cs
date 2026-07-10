namespace PromptOps.Application.Evaluations;

/// <summary>
/// A judge prompt handed back to a delegating MCP client (ADR-0010/Phase 12), awaiting its answer.
/// <see cref="Prompt"/> grows with each correction (see <see cref="JudgePromptBuilder.AppendCorrection"/>)
/// and <see cref="Attempt"/> tracks how many answers have been tried so far, mirroring the retry
/// loop <c>AIJudgeEvaluationProvider</c> runs internally for the autonomous path.
/// </summary>
public sealed record PendingDelegatedEvaluation(Guid ExecutionId, string Prompt, int Attempt, DateTimeOffset ExpiresAtUtc);
