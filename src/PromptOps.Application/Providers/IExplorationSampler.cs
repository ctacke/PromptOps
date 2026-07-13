namespace PromptOps.Application.Providers;

/// <summary>
/// The ε coin flip behind Phase 16c's A/B shadow traffic — isolated behind a port so the
/// randomness stays out of the domain/application logic and is trivially deterministic in tests.
/// </summary>
public interface IExplorationSampler
{
    /// <summary>Returns true with probability <paramref name="rate"/> (clamped to [0,1]); a rate of 0 never explores, 1 always does.</summary>
    bool ShouldExplore(double rate);
}
