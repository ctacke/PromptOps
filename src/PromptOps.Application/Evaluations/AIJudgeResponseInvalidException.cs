namespace PromptOps.Application.Evaluations;

/// <summary>Raised when a judge (<see cref="PromptOps.Application.Providers.IAIEvaluationProvider"/>) never returns a schema-conforming response within its retry budget.</summary>
public sealed class AIJudgeResponseInvalidException(Guid executionId, int attempts, Exception? lastParseError)
    : Exception($"AI judge did not return a valid evaluation for execution '{executionId}' after {attempts} attempt(s).", lastParseError)
{
    public Guid ExecutionId { get; } = executionId;
    public int Attempts { get; } = attempts;
}
