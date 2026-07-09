using PromptOps.Domain.Evaluations;

namespace PromptOps.Application.Providers;

/// <summary>
/// Asks an AI backend whether a recorded execution satisfied its acceptance criteria/ADRs and
/// what could improve the prompt that produced it. Built on <see cref="IAIExecutionProvider"/>.
/// See ADR-0003.
///
/// <paramref name="parameters"/> mirrors <see cref="IMetricCollector"/>'s design (ADR-0003/Phase 5):
/// a real judge implementation mostly ignores it, driving the underlying
/// <see cref="IAIExecutionProvider"/> entirely from the prompt it builds itself, while the
/// reference implementation's only concrete <see cref="IAIExecutionProvider"/> today
/// (<c>ManualAIExecutionProvider</c>) has no model to actually reason with — it just echoes back
/// whatever <c>parameters["output"]</c> supplies, which is how tests and manual invocation drive
/// canned judge responses through the same code path a real judge call would take.
/// </summary>
public interface IAIEvaluationProvider
{
    string Name { get; }

    Task<AIEvaluation> EvaluateAsync(
        Guid executionId,
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken cancellationToken = default);
}
