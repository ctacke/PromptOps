using PromptOps.Application.Providers;
using PromptOps.Domain.Recommendations;

namespace PromptOps.Application.Recommendations;

/// <summary>
/// Application-layer use case for Phase 9: classify-then-recommend. The developer supplies a
/// task description, not tags — classification is internal, not a separate user-facing step
/// (the phase's explicit design goal). What <c>/promptops recommend</c> and the
/// <c>recommend_prompt</c> MCP tool both call.
/// </summary>
public sealed class RecommendationService(
    IActivityClassifier classifier,
    IRecommendationProvider recommendationProvider)
{
    public async Task<IReadOnlyList<Recommendation>> RecommendForTaskAsync(
        string taskDescription,
        string? repository = null,
        int limit = 5,
        IReadOnlyDictionary<string, string>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        var tags = await classifier.ClassifyAsync(taskDescription, parameters ?? new Dictionary<string, string>(), cancellationToken);
        return await recommendationProvider.RecommendAsync(tags, taskDescription, repository, limit, cancellationToken);
    }
}
