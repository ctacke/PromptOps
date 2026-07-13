using PromptOps.Application.Providers;

namespace PromptOps.Infrastructure.Providers;

/// <summary>
/// Default <see cref="IExplorationSampler"/> (Phase 16c): a plain uniform draw against
/// <see cref="Random.Shared"/> (thread-safe). Registered as a singleton — it holds no per-request
/// state. Tests substitute a deterministic sampler instead of relying on this.
/// </summary>
public sealed class RandomExplorationSampler : IExplorationSampler
{
    public bool ShouldExplore(double rate)
    {
        if (rate <= 0) return false;
        if (rate >= 1) return true;
        return Random.Shared.NextDouble() < rate;
    }
}
