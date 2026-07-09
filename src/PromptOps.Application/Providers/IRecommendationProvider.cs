using PromptOps.Domain.Recommendations;

namespace PromptOps.Application.Providers;

/// <summary>
/// Ranks/searches historical prompt versions for a new task, by default across every repository
/// in the shared database (see ADR-0005/§9 of architecture.md), filterable to one repository.
/// See ADR-0003. Returns a stated rationale per result, not a black-box score (Phase 9 acceptance
/// criterion) — that's why this returns <see cref="Recommendation"/>, not just an ordered list of
/// ids.
/// </summary>
public interface IRecommendationProvider
{
    Task<IReadOnlyList<Recommendation>> RecommendAsync(
        IReadOnlyList<string> tags,
        string? repository = null,
        int limit = 5,
        CancellationToken cancellationToken = default);
}
