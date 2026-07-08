namespace PromptOps.Application.Providers;

/// <summary>
/// Computes a prompt version's score from weighted inputs (human rating, engineering metrics,
/// AI evaluation) under a scoring configuration. See ADR-0003. Implemented in Phase 8.
/// </summary>
public interface IScoringProvider
{
    Task<double> ComputeScoreAsync(Guid promptVersionId, CancellationToken cancellationToken = default);
}
