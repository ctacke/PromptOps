namespace PromptOps.Application.Providers;

/// <summary>
/// Ranks/searches historical prompt versions for a new task, by default across every repository
/// in the shared database (see ADR-0005/§9 of architecture.md), filterable to one repository.
/// See ADR-0003. Implemented in Phase 9 (tag/history) and Phase 10 (semantic).
/// </summary>
public interface IRecommendationProvider
{
    Task<IReadOnlyList<Guid>> RecommendAsync(IReadOnlyList<string> tags, CancellationToken cancellationToken = default);
}
