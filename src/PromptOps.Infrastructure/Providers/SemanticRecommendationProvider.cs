using PromptOps.Application.Embeddings;
using PromptOps.Application.Executions;
using PromptOps.Application.Prompts;
using PromptOps.Application.Providers;
using PromptOps.Application.Scoring;
using PromptOps.Domain.Recommendations;

namespace PromptOps.Infrastructure.Providers;

/// <summary>
/// Reference <see cref="IRecommendationProvider"/> v2 (Phase 10, ADR-0003: "SemanticRecommendationProvider").
/// The bound <see cref="IRecommendationProvider"/> as of this phase — blends semantic similarity,
/// tag overlap, and historical <c>PromptScore</c> into one ranking, still spanning every repo in
/// the shared database by default (ADR-0005), same as v1.
///
/// The key structural difference from <see cref="TagAndHistoryRecommendationProvider"/> (v1):
/// this does <b>not</b> exclude candidates with zero tag overlap. Doing so would make the phase's
/// entire acceptance criterion — "semantically similar past tasks surface even without exact tag
/// overlap" — impossible, since the exclusion would run before semantic scoring ever got a
/// chance. <see cref="RecommendationCandidateGatherer"/> is shared with v1 specifically because it
/// doesn't apply that filter; only v1 applies it, on the gathered results it gets back.
/// </summary>
public sealed class SemanticRecommendationProvider(
    IPromptRepository promptRepository,
    IPromptScoreRepository scoreRepository,
    IExecutionRepository executionRepository,
    IEmbeddingProvider embeddingProvider,
    IEmbeddingStore embeddingStore) : IRecommendationProvider
{
    private const double SemanticWeight = 0.4;
    private const double TagWeight = 0.3;
    private const double HistoricalWeight = 0.3;

    /// <summary>Large enough to cover every indexed PromptVersion at this project's stated single-machine scale (architecture.md) — FindSimilarAsync's internal cost is dominated by loading all rows of the subject type regardless of this limit, so there's no real cost to asking generously.</summary>
    private const int MaxEmbeddingMatches = 10_000;

    public async Task<IReadOnlyList<Recommendation>> RecommendAsync(
        IReadOnlyList<string> tags,
        string? taskDescription = null,
        string? repository = null,
        int limit = 5,
        CancellationToken cancellationToken = default)
    {
        var gathered = await RecommendationCandidateGatherer.GatherAsync(
            promptRepository, scoreRepository, executionRepository, tags, repository, cancellationToken);

        var queryText = !string.IsNullOrWhiteSpace(taskDescription) ? taskDescription : string.Join(" ", tags);
        var similarityBySubject = await BuildSimilarityLookupAsync(queryText, cancellationToken);

        // A plain loop, not a LINQ .Select(...), specifically to compute each candidate's
        // similarity exactly once as a true double? (null when absent) and pass that same value
        // to BlendScore — Dictionary<TKey,double>.GetValueOrDefault returns a non-nullable double
        // (0.0 when missing), which would silently turn "no stored embedding yet" into "scored a
        // similarity of zero" if used here instead.
        var scored = new List<(GatheredCandidate Entry, double? Similarity, double Blended)>(gathered.Count);
        foreach (var entry in gathered)
        {
            double? similarity = similarityBySubject.TryGetValue(entry.Candidate.PromptVersionId, out var s) ? s : null;
            scored.Add((entry, similarity, BlendScore(entry, similarity, tags)));
        }

        var ranked = scored
            .OrderByDescending(r => r.Blended)
            .Take(limit)
            .ToList();

        var queryContext = BuildQueryContext(tags, taskDescription, repository);
        var results = new List<Recommendation>(ranked.Count);
        for (var i = 0; i < ranked.Count; i++)
        {
            var (entry, similarity, _) = ranked[i];
            results.Add(new Recommendation(
                queryContext, entry.Candidate.PromptVersionId, BuildRationale(entry, similarity, tags), similarity ?? 0.0, Rank: i + 1));
        }

        return results;
    }

    private async Task<Dictionary<Guid, double>> BuildSimilarityLookupAsync(string queryText, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(queryText))
            return []; // nothing to embed — every candidate's semantic component is excluded, not scored as 0 (see BlendScore)

        var queryEmbedding = await embeddingProvider.EmbedAsync(queryText, cancellationToken);
        var matches = await embeddingStore.FindSimilarAsync(queryEmbedding, EmbeddingSubjectTypes.PromptVersion, MaxEmbeddingMatches, cancellationToken);
        return matches.ToDictionary(m => m.SubjectId, m => m.Similarity);
    }

    /// <summary>
    /// Same principle Phase 8 established for <c>WeightedSumScoringProvider</c>: a component
    /// (semantic similarity, tag overlap, historical score) is only included in the weighted
    /// average if it actually has data — a candidate with no stored embedding yet, or a query with
    /// no tags, doesn't get penalized with an implicit zero for that component.
    /// </summary>
    private static double BlendScore(GatheredCandidate entry, double? similarity, IReadOnlyList<string> tags)
    {
        double weightedSum = 0;
        double totalWeight = 0;

        if (similarity.HasValue)
        {
            weightedSum += similarity.Value * SemanticWeight;
            totalWeight += SemanticWeight;
        }

        if (tags.Count > 0)
        {
            weightedSum += (entry.MatchedTagCount / (double)tags.Count) * TagWeight;
            totalWeight += TagWeight;
        }

        if (entry.Score.HasValue)
        {
            weightedSum += (entry.Score.Value / 100.0) * HistoricalWeight;
            totalWeight += HistoricalWeight;
        }

        return totalWeight > 0 ? weightedSum / totalWeight : 0.0;
    }

    private static string BuildQueryContext(IReadOnlyList<string> tags, string? taskDescription, string? repository)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(taskDescription))
            parts.Add($"task=\"{Truncate(taskDescription, 60)}\"");
        parts.Add(tags.Count > 0 ? $"tags={string.Join(",", tags)}" : "tags=(none)");
        if (repository is not null)
            parts.Add($"repository={repository}");
        return string.Join("; ", parts);
    }

    private static string Truncate(string text, int maxLength) => text.Length <= maxLength ? text : text[..maxLength] + "...";

    private static string BuildRationale(GatheredCandidate entry, double? similarity, IReadOnlyList<string> tags)
    {
        var parts = new List<string>
        {
            similarity.HasValue
                ? $"Semantic similarity: {similarity.Value:F2}."
                : "No semantic signal available (not yet indexed)."
        };

        parts.Add(tags.Count > 0
            ? $"Matched {entry.MatchedTagCount}/{tags.Count} requested tag(s)."
            : "No tags requested.");

        parts.Add(entry.Score.HasValue
            ? $"Score: {entry.Score.Value:F1}/100 from {entry.SampleSize} execution(s) across {entry.RepositoryCount} repo(s)."
            : "Not yet scored — no execution history recorded.");

        return string.Join(" ", parts);
    }
}
