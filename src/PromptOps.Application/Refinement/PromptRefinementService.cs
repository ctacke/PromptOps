using PromptOps.Application.Evaluations;
using PromptOps.Application.Executions;
using PromptOps.Application.Prompts;
using PromptOps.Application.Providers;
using PromptOps.Domain.Prompts;

namespace PromptOps.Application.Refinement;

/// <summary>
/// Phase 16a: turns an AI judge's <c>SuggestedPromptImprovements</c> into a new Draft
/// <c>PromptVersion</c> — closing the gap where suggestions were produced and stored but nothing
/// ever acted on them. The <c>AutoPromotionTrigger</c> (Phase 11) already waits for exactly such a
/// Draft candidate; this is what finally produces one.
///
/// Kept as an ordinary application service (no threading, no DI-scope juggling) so it's unit-testable
/// in isolation; <c>PromptRefinementTrigger</c> is the thin Infrastructure adapter that runs it
/// detached off the <c>AIEvaluationRecorded</c> event, mirroring how <c>AutoAIEvaluationTrigger</c>
/// delegates to <c>AIEvaluationService</c>.
/// </summary>
public sealed class PromptRefinementService(
    IAIEvaluationRepository evaluationRepository,
    IExecutionRepository executionRepository,
    IPromptRepository promptRepository,
    IPromptRefinementProvider refinementProvider,
    PromptService promptService)
{
    /// <summary>The <c>createdBy</c> stamped on auto-refined versions, distinguishing them from human-authored ones.</summary>
    public const string RefinerActor = "promptops-refinement";

    public async Task<RefinementResult> RefineFromEvaluationAsync(
        Guid evaluationId,
        Guid executionId,
        CancellationToken cancellationToken = default)
    {
        var evaluations = await evaluationRepository.GetByExecutionIdAsync(executionId, cancellationToken);
        var evaluation = evaluations.FirstOrDefault(e => e.Id == evaluationId);
        if (evaluation is null)
            return new RefinementResult(RefinementOutcome.NoEvaluation);

        if (evaluation.SuggestedPromptImprovements.Count == 0)
            return new RefinementResult(RefinementOutcome.NoSuggestions);

        var execution = await executionRepository.GetByIdAsync(executionId, cancellationToken);
        if (execution is null)
            return new RefinementResult(RefinementOutcome.ExecutionNotFound);

        // An untracked execution (all-zeros) resolves to no prompt — nothing to refine. This is why
        // Phase 15 attribution is a prerequisite: only attributed executions can be improved.
        if (execution.PromptVersionId == Guid.Empty)
            return new RefinementResult(RefinementOutcome.Untracked);

        var prompt = await promptRepository.GetByVersionIdAsync(execution.PromptVersionId, cancellationToken);
        var version = prompt?.Versions.FirstOrDefault(v => v.Id == execution.PromptVersionId);
        if (prompt is null || version is null)
            return new RefinementResult(RefinementOutcome.Untracked);

        // Only refine the live prompt — never a Draft (would fork a fork) or a deliberately
        // Deprecated version.
        if (version.Status != PromptVersionStatus.Active)
            return new RefinementResult(RefinementOutcome.VersionNotActive);

        // Anti-runaway: at most one refinement candidate in flight per prompt. If a Draft already
        // exists (from an earlier refinement awaiting benchmark/promotion, or a human), don't pile on.
        if (prompt.Versions.Any(v => v.Status == PromptVersionStatus.Draft))
            return new RefinementResult(RefinementOutcome.CandidateAlreadyExists);

        var refined = await refinementProvider.RefineAsync(version.Content, evaluation.SuggestedPromptImprovements, cancellationToken);
        if (string.IsNullOrWhiteSpace(refined) || string.Equals(refined.Trim(), version.Content.Trim(), StringComparison.Ordinal))
            return new RefinementResult(RefinementOutcome.NoContentChange);

        var draft = await promptService.CreateVersionAsync(
            prompt.Id,
            refined,
            RefinerActor,
            changelogEntry: "Auto-refined from AI evaluation suggestions (Phase 16a).",
            cancellationToken: cancellationToken);

        return new RefinementResult(RefinementOutcome.Drafted, draft.Id, prompt.Id);
    }
}

/// <summary>Why <see cref="PromptRefinementService.RefineFromEvaluationAsync"/> did or didn't draft a new version — the trigger logs this.</summary>
public enum RefinementOutcome
{
    NoEvaluation,
    NoSuggestions,
    ExecutionNotFound,
    Untracked,
    VersionNotActive,
    CandidateAlreadyExists,
    NoContentChange,
    Drafted
}

public sealed record RefinementResult(RefinementOutcome Outcome, Guid? DraftVersionId = null, Guid? PromptId = null);
