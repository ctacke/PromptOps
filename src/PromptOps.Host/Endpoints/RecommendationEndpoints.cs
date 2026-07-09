using PromptOps.Application.Recommendations;
using PromptOps.Domain.Recommendations;

namespace PromptOps.Host.Endpoints;

/// <summary>Recommendation engine (Phase 9): classify-then-recommend over the ingestion API — the same <see cref="RecommendationService"/> the <c>recommend_prompt</c> MCP tool (<see cref="Mcp.RecommendationTools"/>) calls.</summary>
public static class RecommendationEndpoints
{
    public static void MapRecommendationEndpoints(this WebApplication app)
    {
        app.MapPost("/recommendations", async (RecommendRequest request, RecommendationService service, CancellationToken cancellationToken) =>
        {
            var recommendations = await service.RecommendForTaskAsync(
                request.TaskDescription, request.Repository, request.Limit ?? 5, request.Parameters, cancellationToken);

            return Results.Ok(recommendations.Select(RecommendationResponse.From));
        });
    }
}

internal sealed record RecommendRequest(string TaskDescription, string? Repository, int? Limit, Dictionary<string, string>? Parameters);

internal sealed record RecommendationResponse(string QueryContext, Guid RecommendedPromptVersionId, string Rationale, double SimilarityScore, int Rank)
{
    public static RecommendationResponse From(Recommendation recommendation) => new(
        recommendation.QueryContext, recommendation.RecommendedPromptVersionId, recommendation.Rationale,
        recommendation.SimilarityScore, recommendation.Rank);
}
