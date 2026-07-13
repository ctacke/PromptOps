using PromptOps.Application.Prompts;
using PromptOps.Application.Providers;
using PromptOps.Domain.Executions;
using PromptOps.Domain.Prompts;

namespace PromptOps.Application.Executions;

/// <summary>
/// Phase 15: decides which <see cref="Domain.Prompts.PromptVersion"/> a just-starting session should
/// be attributed to, then opens the <see cref="ExecutionRecord"/> against it — the step
/// architecture.md §5 (1) always intended ("resolve the PromptVersion to use … and open an
/// ExecutionRecord") but that the SessionStart hook stubbed out by always passing the all-zeros
/// "untracked" id. Attribution has to happen at start time because
/// <see cref="ExecutionRecord.PromptVersionId"/> is immutable once the record is created, so this
/// runs on the first user prompt (when a task description finally exists), not at SessionStart
/// (when it doesn't).
///
/// Three outcomes:
/// <list type="bullet">
/// <item><b>untracked</b> — the task doesn't classify as a development activity (empty tag list);
/// opened against <see cref="UntrackedPromptVersionId"/> exactly as before, so non-dev chatter never
/// pollutes the prompt library.</item>
/// <item><b>recommended</b> — an existing prompt shares the task's activity; the execution is
/// attributed to it and its content is surfaced so the agent can actually use it in-session.</item>
/// <item><b>captured</b> — a development task with no existing prompt for that activity; the
/// developer's own prompt is captured as a new, Active prompt and the execution is attributed to it,
/// so day-one tasks feed the loop instead of being lost.</item>
/// </list>
///
/// The recommend-vs-capture decision keys off <em>activity</em> tags specifically (see
/// <see cref="ActivityTags"/>), not raw semantic similarity: "debug this" must capture a new
/// debugging prompt even when a "create a feature" prompt already exists, per the product intent.
/// Interactive <c>/promptops recommend</c> keeps using the full semantic ranking
/// (<see cref="Recommendations.RecommendationService"/>) — that's a suggestion surface, this is an
/// attribution decision, and they legitimately want different behavior.
/// </summary>
public sealed class ExecutionAttributionService(
    IActivityClassifier classifier,
    IPromptRepository promptRepository,
    PromptService promptService,
    ExecutionService executionService,
    Refinement.AbVersionSelector abVersionSelector)
{
    /// <summary>The all-zeros sentinel meaning "no prompt version" — mirrors the plugin's <c>UNTRACKED_PROMPT_VERSION_ID</c> (claude-plugin/hooks/lib/state.mjs).</summary>
    public static readonly Guid UntrackedPromptVersionId = Guid.Empty;

    /// <summary>
    /// Canonical software-development activity tags (mirrors the list <c>AIActivityClassifier</c>
    /// prompts with). Only overlap on these decides recommend-vs-capture, so an incidental shared
    /// tag like a language ("csharp") never makes "debug this" attribute to a "create a feature"
    /// prompt.
    /// </summary>
    private static readonly HashSet<string> ActivityTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "debugging", "testing", "code-authoring", "refactoring",
        "documentation", "code-review", "performance", "security"
    };

    public async Task<AttributedExecution> StartAttributedAsync(
        string taskDescription,
        string rawPrompt,
        string developerId,
        DevelopmentContext context,
        IReadOnlyDictionary<string, string>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        var tags = await classifier.ClassifyAsync(taskDescription, parameters ?? new Dictionary<string, string>(), cancellationToken);

        // Non-development (or unclassifiable) task → don't track against a prompt, don't capture.
        if (tags.Count == 0)
            return await StartUntrackedAsync(developerId, context, cancellationToken);

        var candidates = await promptRepository.GetRecommendationCandidatesAsync(cancellationToken);
        var match = BestActivityMatch(candidates, tags);

        if (match is not null)
        {
            // Phase 16c: with probability RefinementPolicy.AbExplorationRate, route this session to an
            // A/B-eligible refined draft of the matched prompt instead of its active version, so the
            // draft earns a real score from live traffic (AutoPromotionTrigger then promotes on evidence).
            var versionId = await abVersionSelector.SelectVersionAsync(match.PromptVersionId, cancellationToken);
            var exploring = versionId != match.PromptVersionId;

            var detail = await promptService.GetVersionDetailAsync(versionId, cancellationToken);
            var execution = await executionService.StartExecutionAsync(versionId, developerId, context, inputs: null, cancellationToken);
            var rationale = exploring
                ? $"Trying an experimental refined variant of '{match.Name}' (A/B shadow traffic) to evaluate whether it outperforms the current version."
                : $"Matched existing prompt '{match.Name}' on activity tag(s): {string.Join(", ", MatchingTags(match.Tags, tags))}.";
            return new AttributedExecution(execution.Id, versionId, AttributionKind.Recommended, detail?.Content, rationale);
        }

        // Development task with no prompt for this activity → capture the developer's prompt as one.
        var capturedName = PromptNameFor(tags);
        var capturedVersionId = await CaptureNewPromptAsync(capturedName, rawPrompt, developerId, tags, cancellationToken);
        var capturedExecution = await executionService.StartExecutionAsync(capturedVersionId, developerId, context, inputs: null, cancellationToken);
        return new AttributedExecution(capturedExecution.Id, capturedVersionId, AttributionKind.Captured, null,
            $"Captured your prompt as a new reusable '{capturedName}' prompt and started tracking its outcomes.");
    }

    private async Task<AttributedExecution> StartUntrackedAsync(string developerId, DevelopmentContext context, CancellationToken cancellationToken)
    {
        var execution = await executionService.StartExecutionAsync(UntrackedPromptVersionId, developerId, context, inputs: null, cancellationToken);
        return new AttributedExecution(execution.Id, UntrackedPromptVersionId, AttributionKind.Untracked, null, null);
    }

    private async Task<Guid> CaptureNewPromptAsync(string name, string rawPrompt, string developerId, IReadOnlyList<string> tags, CancellationToken cancellationToken)
    {
        var metadata = new PromptMetadata { Description = $"Auto-captured from a {name} task.", Tags = tags };
        var prompt = await promptService.CreatePromptAsync(name, metadata, cancellationToken);
        var version = await promptService.CreateVersionAsync(
            prompt.Id, rawPrompt, developerId, changelogEntry: "Auto-captured from a live session (Phase 15).", cancellationToken: cancellationToken);
        // Activate so it becomes the recommendable candidate for this activity (GetRecommendationCandidatesAsync
        // prefers the Active version) — the next same-activity task then attributes to it rather than re-capturing.
        await promptService.ActivateVersionAsync(prompt.Id, version.Id, cancellationToken);
        return version.Id;
    }

    private static PromptRecommendationCandidate? BestActivityMatch(IReadOnlyList<PromptRecommendationCandidate> candidates, IReadOnlyList<string> tags)
    {
        var taskActivities = tags.Where(ActivityTags.Contains).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // If the classifier produced no canonical activity tag, fall back to full-tag overlap so an
        // invented-but-shared tag can still match rather than force a needless capture.
        var useFullTagOverlap = taskActivities.Count == 0;

        return candidates
            .Select(candidate => (candidate, overlap: useFullTagOverlap
                ? candidate.Tags.Count(t => tags.Contains(t, StringComparer.OrdinalIgnoreCase))
                : candidate.Tags.Count(taskActivities.Contains)))
            .Where(x => x.overlap > 0)
            .OrderByDescending(x => x.overlap)
            .ThenBy(x => x.candidate.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.candidate)
            .FirstOrDefault();
    }

    private static IEnumerable<string> MatchingTags(IReadOnlyList<string> candidateTags, IReadOnlyList<string> taskTags)
        => candidateTags.Where(t => taskTags.Contains(t, StringComparer.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase);

    private static string PromptNameFor(IReadOnlyList<string> tags)
    {
        var activity = tags.FirstOrDefault(ActivityTags.Contains) ?? tags[0];
        // "code-authoring" -> "Code Authoring"
        return string.Join(" ", activity
            .Split('-', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => char.ToUpperInvariant(word[0]) + word[1..]));
    }
}

/// <summary>Which of the three attribution outcomes <see cref="ExecutionAttributionService"/> took.</summary>
public enum AttributionKind
{
    Untracked,
    Recommended,
    Captured
}

/// <summary>The opened execution plus how it was attributed — <see cref="Content"/>/<see cref="Rationale"/> are populated for the recommended/captured cases so the hook can surface them in-session.</summary>
public sealed record AttributedExecution(
    Guid ExecutionId,
    Guid PromptVersionId,
    AttributionKind Attribution,
    string? Content,
    string? Rationale);
