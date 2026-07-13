using System.Text.Json;
using PromptOps.Application.Evaluations;
using PromptOps.Application.Providers;

namespace PromptOps.Infrastructure.Providers;

/// <summary>
/// Reference <see cref="IActivityClassifier"/> (Phase 9): asks the configured
/// <see cref="IAIExecutionProvider"/> to classify a free-text task description into activity tags,
/// reusing <see cref="AIJudgeEvaluationProvider"/>'s resilience pattern — <see cref="JsonExtraction"/>
/// tolerates markdown fences/prose, and a malformed response gets retried with the parse error fed
/// back as a correction, up to <see cref="MaxAttempts"/> times.
///
/// Deliberately diverges from the judge on what happens when every attempt still fails:
/// <see cref="AIJudgeEvaluationProvider"/> throws (a missing AI evaluation is a data-integrity
/// concern worth surfacing), but a failed classification just means "recommend broadly instead of
/// narrowly" — <see cref="IRecommendationProvider"/> already treats an empty tag list as "no
/// filter" (see <c>TagAndHistoryRecommendationProvider</c>), so degrading to zero tags here is a
/// graceful fallback, not a pipeline failure that would block the developer from getting any
/// recommendation at all.
/// </summary>
public sealed class AIActivityClassifier(IAIExecutionProvider aiExecutionProvider) : IActivityClassifier
{
    private const int MaxAttempts = 3;
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<string>> ClassifyAsync(
        string taskDescription,
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken cancellationToken = default)
    {
        var prompt = BuildClassificationPrompt(taskDescription);

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var raw = await aiExecutionProvider.ExecuteAsync(prompt, parameters, cancellationToken);

            if (TryParseTags(raw, out var tags, out var parseError))
                return tags!;

            prompt = AppendCorrection(prompt, raw, parseError);
        }

        return [];
    }

    private static string BuildClassificationPrompt(string taskDescription) => $"""
        You are classifying a developer's task description into activity tags for a prompt-recommendation system.

        --- Task description ---
        {taskDescription}
        --- end task description ---

        Common tags include (but are not limited to): debugging, testing, code-authoring, refactoring, documentation, code-review, performance, security. Pick whichever tags genuinely apply — invent new lowercase, hyphenated tags if none of these fit.

        If the task is NOT a software-development activity (e.g. a general-knowledge question, casual conversation, or anything a developer wouldn't do to a codebase), respond with an empty array: []. An empty array is the correct answer for non-development tasks — this is what keeps them from being tracked or captured as prompts (Phase 15).

        Respond with ONLY a JSON array of lowercase, hyphenated tag strings, no other text, no markdown fences. Example: ["debugging", "csharp"]
        """;

    private static string AppendCorrection(string prompt, string invalidResponse, Exception? parseError) => $"""
        {prompt}

        Your previous response could not be parsed as a JSON array of strings (error: {parseError?.Message ?? "unknown"}). Your previous response was:
        {invalidResponse}

        Respond again with ONLY a valid JSON array of tag strings — no markdown fences, no extra text.
        """;

    private static bool TryParseTags(string raw, out IReadOnlyList<string>? tags, out Exception? error)
    {
        tags = null;
        error = null;

        var json = JsonExtraction.ExtractJsonValue(raw);
        if (json is null)
        {
            error = new FormatException("No JSON array found in the response.");
            return false;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<List<string>>(json, SerializerOptions);
            if (parsed is null)
            {
                error = new FormatException("Response deserialized to null.");
                return false;
            }

            tags = parsed.Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
            return true;
        }
        catch (JsonException ex)
        {
            error = ex;
            return false;
        }
    }
}
