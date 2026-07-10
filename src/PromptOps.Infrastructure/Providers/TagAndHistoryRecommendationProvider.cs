using PromptOps.Application.Executions;
using PromptOps.Application.Prompts;
using PromptOps.Application.Providers;
using PromptOps.Application.Scoring;
using PromptOps.Domain.Recommendations;

namespace PromptOps.Infrastructure.Providers;

/// <summary>
/// Reference <see cref="IRecommendationProvider"/> v1 (Phase 9, ADR-0003:
/// "TagAndHistoryRecommendationProvider"). Ranks by tag overlap with
/// <see cref="PromptOps.Domain.Scoring.PromptScore"/>'s latest <c>OverallScore</c> (Phase 8) as
/// the quality signal — across every repo in the shared database by default (ADR-0005), narrowed
/// to one repo only if <paramref name="repository"/> is given. Every result carries a
/// human-readable rationale (Phase 9's "not a black-box score" acceptance criterion).
///
/// Still registered as a concrete type (not the bound <c>IRecommendationProvider</c>) after
/// Phase 10 — <see cref="SemanticRecommendationProvider"/> (v2) is what the application actually
/// uses now, but v1 stays as-is, directly testable and available, since <c>ignores taskDescription</c>
/// is a perfectly legitimate provider for anyone who only has tags and no free text.
/// </summary>
public sealed class TagAndHistoryRecommendationProvider(
    IPromptRepository promptRepository,
    IPromptScoreRepository scoreRepository,
    IExecutionRepository executionRepository) : IRecommendationProvider
{
    public async Task<IReadOnlyList<Recommendation>> RecommendAsync(
        IReadOnlyList<string> tags,
        string? taskDescription = null,
        string? repository = null,
        int limit = 5,
        CancellationToken cancellationToken = default)
    {
        var gathered = await RecommendationCandidateGatherer.GatherAsync(
            promptRepository, scoreRepository, executionRepository, tags, repository, cancellationToken);

        // No tags requested = no filter (match everything); tags requested but zero overlap = excluded.
        var ranked = gathered
            .Where(g => tags.Count == 0 || g.MatchedTagCount > 0)
            .OrderByDescending(g => g.Score.HasValue) // scored candidates before never-scored ones
            .ThenByDescending(g => g.Score ?? 0)
            .ThenByDescending(g => g.MatchedTagCount)
            .Take(limit)
            .ToList();

        var queryContext = BuildQueryContext(tags, repository);
        var results = new List<Recommendation>(ranked.Count);
        for (var i = 0; i < ranked.Count; i++)
        {
            var entry = ranked[i];
            var similarityScore = tags.Count > 0 ? (double)entry.MatchedTagCount / tags.Count : 1.0;
            results.Add(new Recommendation(queryContext, entry.Candidate.PromptVersionId, BuildRationale(entry, tags), similarityScore, Rank: i + 1));
        }

        return results;
    }

    private static string BuildQueryContext(IReadOnlyList<string> tags, string? repository)
    {
        var context = tags.Count > 0 ? $"tags={string.Join(",", tags)}" : "tags=(none)";
        return repository is null ? context : $"{context}; repository={repository}";
    }

    private static string BuildRationale(GatheredCandidate entry, IReadOnlyList<string> tags)
    {
        var tagPart = tags.Count > 0
            ? $"Matched {entry.MatchedTagCount}/{tags.Count} requested tag(s)."
            : "No tags requested — showing all scored prompt versions.";

        var scorePart = entry.Score.HasValue
            ? $"Score: {entry.Score.Value:F1}/100 from {entry.SampleSize} execution(s) across {entry.RepositoryCount} repo(s)."
            : "Not yet scored — no execution history recorded.";

        return $"{tagPart} {scorePart}";
    }
}
