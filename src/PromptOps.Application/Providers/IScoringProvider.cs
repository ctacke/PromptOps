using PromptOps.Domain.Scoring;

namespace PromptOps.Application.Providers;

/// <summary>
/// Computes a prompt version's score from weighted inputs (human rating, engineering metrics,
/// AI evaluation) under a scoring configuration. See ADR-0003.
///
/// Returns a constructed-but-not-yet-persisted <see cref="PromptScore"/> — the same "provider
/// computes, service persists + publishes events" split used by
/// <see cref="IAIEvaluationProvider"/> (Phase 7): a provider implementation shouldn't need to know
/// about repositories or the event publisher to do its actual job.
/// </summary>
public interface IScoringProvider
{
    Task<PromptScore> ComputeAsync(Guid promptVersionId, ScoringConfig config, CancellationToken cancellationToken = default);
}
