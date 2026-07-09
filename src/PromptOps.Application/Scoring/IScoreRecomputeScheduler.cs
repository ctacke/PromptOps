namespace PromptOps.Application.Scoring;

/// <summary>
/// Debounced "recompute this prompt version's score soon" signal (Phase 8: "recompute-on-event
/// (debounced)"). Domain event handlers call <see cref="RequestRecompute"/> whenever something
/// that feeds scoring changes (an execution finishes, metrics/evaluations arrive); the
/// implementation collapses rapid-fire requests for the same prompt version into a single
/// recompute after a quiet period, rather than recomputing once per event.
/// </summary>
public interface IScoreRecomputeScheduler
{
    void RequestRecompute(Guid promptVersionId);
}
