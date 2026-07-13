using PromptOps.Application.Providers;

namespace PromptOps.Application.Refinement;

/// <summary>
/// Phase 16c: decides, per session, whether to route an attributed execution to an A/B-eligible
/// refined Draft (giving it real shadow traffic) instead of the active version. With probability
/// <c>RefinementPolicy.AbExplorationRate</c>, a prompt that has a benchmark-passing draft
/// (<c>RefinementCandidateStatus.AbEligible</c>) is served that draft; otherwise the active version.
/// The resulting execution is scored like any other, so once the draft accumulates enough favorable
/// evidence the existing <c>AutoPromotionTrigger</c> (Phase 11) promotes it — no new promotion logic.
///
/// Applied at attribution time (<see cref="Executions.ExecutionAttributionService"/>), which is where
/// a session's <c>PromptVersionId</c> is actually assigned — the interactive <c>/promptops recommend</c>
/// surface is left showing the proven active version, since it creates no executions to earn a score.
/// </summary>
public sealed class AbVersionSelector(
    IRefinementPolicyRepository policyRepository,
    IRefinementCandidateRepository candidateRepository,
    IExplorationSampler sampler)
{
    /// <summary>Returns the version id to attribute to — either <paramref name="activeVersionId"/> or, on an exploration draw, an A/B-eligible draft of it.</summary>
    public async Task<Guid> SelectVersionAsync(Guid activeVersionId, CancellationToken cancellationToken = default)
    {
        var policy = await policyRepository.GetAsync(cancellationToken);
        var rate = policy?.AbExplorationRate ?? 0;
        if (rate <= 0 || !sampler.ShouldExplore(rate))
            return activeVersionId;

        var eligible = await candidateRepository.GetAbEligibleByActiveVersionIdAsync(activeVersionId, cancellationToken);
        // First eligible candidate wins; the 16a dedup guard keeps at most one live draft per prompt,
        // so there's normally only ever zero or one here anyway.
        return eligible.Count == 0 ? activeVersionId : eligible[0].DraftVersionId;
    }
}
