namespace PromptOps.Application.Providers;

/// <summary>
/// Asks an AI backend whether a recorded execution satisfied its acceptance criteria/ADRs and
/// what could improve the prompt that produced it. Built on <see cref="IAIExecutionProvider"/>.
/// See ADR-0003. Implemented in Phase 7.
/// </summary>
public interface IAIEvaluationProvider
{
    Task<string> EvaluateAsync(Guid executionId, CancellationToken cancellationToken = default);
}
