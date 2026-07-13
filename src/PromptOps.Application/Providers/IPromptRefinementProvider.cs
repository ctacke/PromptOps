namespace PromptOps.Application.Providers;

/// <summary>
/// Drafts improved prompt content from an AI judge's suggestions (Phase 16a). Like
/// <c>IAIEvaluationProvider</c> and <c>IActivityClassifier</c>, the reference implementation is built
/// on <c>IAIExecutionProvider</c> — "refine this prompt" is just another prompt execution — so no
/// separate AI dependency is introduced.
/// </summary>
public interface IPromptRefinementProvider
{
    /// <summary>
    /// Returns rewritten prompt content incorporating <paramref name="suggestions"/>, or an empty
    /// string if it can't produce a meaningful improvement (the caller treats empty / unchanged
    /// output as "no draft"). Must not throw for an ordinary "no improvement" outcome.
    /// </summary>
    Task<string> RefineAsync(
        string currentContent,
        IReadOnlyList<string> suggestions,
        CancellationToken cancellationToken = default);
}
