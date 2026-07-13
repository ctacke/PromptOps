using PromptOps.Application.Providers;

namespace PromptOps.Infrastructure.Providers;

/// <summary>
/// Reference <see cref="IPromptRefinementProvider"/> (Phase 16a): asks the configured
/// <see cref="IAIExecutionProvider"/> to rewrite a prompt so that following it would address the
/// judge's suggestions — the same "reuse the execution provider, don't add a new AI dependency"
/// pattern as <see cref="AIJudgeEvaluationProvider"/> and <see cref="AIActivityClassifier"/>.
///
/// Output is free-form prompt text (not JSON), so there's no schema to validate — the caller
/// (<c>PromptRefinementService</c>) decides whether the result is a meaningful change. A blank
/// response degrades to "no draft" rather than throwing, since a missing improvement is not a
/// data-integrity concern (contrast the judge, which throws).
/// </summary>
public sealed class AIPromptRefinementProvider(IAIExecutionProvider aiExecutionProvider) : IPromptRefinementProvider
{
    public async Task<string> RefineAsync(
        string currentContent,
        IReadOnlyList<string> suggestions,
        CancellationToken cancellationToken = default)
    {
        var prompt = BuildRefinementPrompt(currentContent, suggestions);
        var raw = await aiExecutionProvider.ExecuteAsync(prompt, new Dictionary<string, string>(), cancellationToken);
        return StripFences(raw).Trim();
    }

    private static string BuildRefinementPrompt(string currentContent, IReadOnlyList<string> suggestions)
    {
        var numbered = string.Join("\n", suggestions.Select((s, i) => $"{i + 1}. {s}"));
        return $"""
            You are improving a reusable developer prompt based on specific feedback from an automated code-quality judge that reviewed an execution which used this prompt.

            --- Current prompt ---
            {currentContent}
            --- end current prompt ---

            Suggested improvements:
            {numbered}

            Rewrite the prompt so that following it would address every suggestion above, while preserving its original intent and any template variables/placeholders it uses. Keep it a general, reusable prompt — do not bake in details specific to the one task that was reviewed.

            Respond with ONLY the full text of the improved prompt — no preamble, no explanation, no markdown fences.
            """;
    }

    /// <summary>Removes a surrounding ```/```lang code fence if the model wrapped the whole response in one, despite being asked not to.</summary>
    private static string StripFences(string raw)
    {
        var text = raw.Trim();
        if (!text.StartsWith("```", StringComparison.Ordinal))
            return raw;

        var firstNewline = text.IndexOf('\n');
        if (firstNewline < 0)
            return raw;

        var body = text[(firstNewline + 1)..];
        var closingFence = body.LastIndexOf("```", StringComparison.Ordinal);
        return closingFence < 0 ? body : body[..closingFence];
    }
}
