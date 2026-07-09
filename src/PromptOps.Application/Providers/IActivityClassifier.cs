namespace PromptOps.Application.Providers;

/// <summary>
/// Classifies a free-text task description into activity tags (e.g. "debugging",
/// "code-authoring") before recommendation runs (ADR-0003) — the input
/// <see cref="IRecommendationProvider"/> needs but that nothing else produces for a session
/// that's just starting. Built on <see cref="IAIExecutionProvider"/>, the same "reuse the
/// provider abstraction" pattern as <see cref="IAIEvaluationProvider"/> — no separate AI
/// dependency.
///
/// <paramref name="parameters"/> mirrors <see cref="IAIEvaluationProvider"/>'s design: a real
/// classifier mostly ignores it, driving everything from the prompt it builds itself; it's how
/// tests and manual/API invocation drive canned classification responses through
/// <c>ManualAIExecutionProvider</c>, the only concrete <see cref="IAIExecutionProvider"/> today.
/// </summary>
public interface IActivityClassifier
{
    Task<IReadOnlyList<string>> ClassifyAsync(
        string taskDescription,
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken cancellationToken = default);
}
