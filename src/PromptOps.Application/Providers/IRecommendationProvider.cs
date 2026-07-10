using PromptOps.Domain.Recommendations;

namespace PromptOps.Application.Providers;

/// <summary>
/// Ranks/searches historical prompt versions for a new task, by default across every repository
/// in the shared database (see ADR-0005/§9 of architecture.md), filterable to one repository.
/// See ADR-0003. Returns a stated rationale per result, not a black-box score (Phase 9 acceptance
/// criterion) — that's why this returns <see cref="Recommendation"/>, not just an ordered list of
/// ids.
///
/// <paramref name="taskDescription"/> was added in Phase 10: a purely tag-based implementation
/// (<c>TagAndHistoryRecommendationProvider</c>, v1) never needed the original free text, but
/// semantic matching (<c>SemanticRecommendationProvider</c>, v2) needs *something* to embed for
/// the query side, and tags alone lose information the original description had. Optional and
/// ignored by v1 so it keeps working unchanged.
/// </summary>
public interface IRecommendationProvider
{
    Task<IReadOnlyList<Recommendation>> RecommendAsync(
        IReadOnlyList<string> tags,
        string? taskDescription = null,
        string? repository = null,
        int limit = 5,
        CancellationToken cancellationToken = default);
}
