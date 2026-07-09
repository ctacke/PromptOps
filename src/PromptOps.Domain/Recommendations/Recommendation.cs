namespace PromptOps.Domain.Recommendations;

/// <summary>
/// One ranked result from <c>IRecommendationProvider</c> (architecture.md §3) — a query-time
/// result, not persisted long-term (unlike every other aggregate in this project, there's no
/// repository for this type; a fresh set is computed on every call). A plain value object: no
/// invariants to protect, nothing to validate — it's a shaped answer, not something that can be
/// created invalidly.
/// </summary>
public sealed record Recommendation(
    string QueryContext,
    Guid RecommendedPromptVersionId,
    string Rationale,
    double SimilarityScore,
    int Rank);
