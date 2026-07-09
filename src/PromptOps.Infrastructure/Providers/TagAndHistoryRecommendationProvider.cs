using PromptOps.Application.Executions;
using PromptOps.Application.Prompts;
using PromptOps.Application.Providers;
using PromptOps.Application.Scoring;
using PromptOps.Domain.Recommendations;

namespace PromptOps.Infrastructure.Providers;

/// <summary>
/// Reference <see cref="IRecommendationProvider"/> (Phase 9, ADR-0003: "TagAndHistoryRecommendationProvider").
/// Ranks by tag overlap with <see cref="PromptOps.Domain.Scoring.PromptScore"/>'s latest
/// <c>OverallScore</c> (Phase 8) as the quality signal — across every repo in the shared database
/// by default (ADR-0005), narrowed to one repo only if <paramref name="repository"/> is given
/// (see <see cref="RecommendAsync"/>'s params). Every result carries a human-readable rationale
/// (Phase 9's explicit "not a black-box score" acceptance criterion) instead of a bare number.
/// </summary>
public sealed class TagAndHistoryRecommendationProvider(
    IPromptRepository promptRepository,
    IPromptScoreRepository scoreRepository,
    IExecutionRepository executionRepository) : IRecommendationProvider
{
    public async Task<IReadOnlyList<Recommendation>> RecommendAsync(
        IReadOnlyList<string> tags,
        string? repository = null,
        int limit = 5,
        CancellationToken cancellationToken = default)
    {
        var candidates = await promptRepository.GetRecommendationCandidatesAsync(cancellationToken);
        var entries = new List<RankingEntry>();

        foreach (var candidate in candidates)
        {
            // No tags requested = no filter (match everything); tags requested but zero overlap = excluded.
            var matchedTagCount = candidate.Tags.Count(t => tags.Contains(t, StringComparer.OrdinalIgnoreCase));
            if (tags.Count > 0 && matchedTagCount == 0)
                continue;

            var executions = await executionRepository.GetByPromptVersionIdAsync(candidate.PromptVersionId, cancellationToken);
            if (repository is not null && !executions.Any(e => string.Equals(e.Context.Repository, repository, StringComparison.OrdinalIgnoreCase)))
                continue;

            var scoreHistory = await scoreRepository.GetByPromptVersionIdAsync(candidate.PromptVersionId, cancellationToken);
            var latestScore = scoreHistory.Count == 0 ? null : scoreHistory[^1]; // chronological — last is most recent

            var repositoryCount = executions.Select(e => e.Context.Repository).Distinct(StringComparer.OrdinalIgnoreCase).Count();

            entries.Add(new RankingEntry(candidate, matchedTagCount, latestScore?.OverallScore, latestScore?.SampleSize ?? 0, repositoryCount));
        }

        var ranked = entries
            .OrderByDescending(e => e.Score.HasValue) // scored candidates before never-scored ones
            .ThenByDescending(e => e.Score ?? 0)
            .ThenByDescending(e => e.MatchedTagCount)
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

    private static string BuildRationale(RankingEntry entry, IReadOnlyList<string> tags)
    {
        var tagPart = tags.Count > 0
            ? $"Matched {entry.MatchedTagCount}/{tags.Count} requested tag(s)."
            : "No tags requested — showing all scored prompt versions.";

        var scorePart = entry.Score.HasValue
            ? $"Score: {entry.Score.Value:F1}/100 from {entry.SampleSize} execution(s) across {entry.RepositoryCount} repo(s)."
            : "Not yet scored — no execution history recorded.";

        return $"{tagPart} {scorePart}";
    }

    private sealed record RankingEntry(PromptRecommendationCandidate Candidate, int MatchedTagCount, double? Score, int SampleSize, int RepositoryCount);
}
