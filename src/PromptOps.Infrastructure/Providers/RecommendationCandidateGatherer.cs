using PromptOps.Application.Executions;
using PromptOps.Application.Prompts;
using PromptOps.Application.Scoring;

namespace PromptOps.Infrastructure.Providers;

/// <summary>
/// Shared by <see cref="TagAndHistoryRecommendationProvider"/> (v1) and
/// <see cref="SemanticRecommendationProvider"/> (v2, Phase 10) — both need the same raw material
/// (every candidate, its tag-match count against the query, its latest <c>PromptScore</c>, its
/// repository footprint), they just differ in how they filter/rank/explain it afterward. Applies
/// the repository filter here (both providers want that exclusion identically) but deliberately
/// <b>not</b> the tag filter — v1 excludes zero-match candidates itself, v2 must not, since
/// including a candidate on semantic similarity alone despite zero tag overlap is the entire point
/// of Phase 10's acceptance criterion.
/// </summary>
internal static class RecommendationCandidateGatherer
{
    public static async Task<IReadOnlyList<GatheredCandidate>> GatherAsync(
        IPromptRepository promptRepository,
        IPromptScoreRepository scoreRepository,
        IExecutionRepository executionRepository,
        IReadOnlyList<string> tags,
        string? repository,
        CancellationToken cancellationToken)
    {
        var candidates = await promptRepository.GetRecommendationCandidatesAsync(cancellationToken);
        var gathered = new List<GatheredCandidate>();

        foreach (var candidate in candidates)
        {
            var matchedTagCount = candidate.Tags.Count(t => tags.Contains(t, StringComparer.OrdinalIgnoreCase));

            var executions = await executionRepository.GetByPromptVersionIdAsync(candidate.PromptVersionId, cancellationToken);
            if (repository is not null && !executions.Any(e => string.Equals(e.Context.Repository, repository, StringComparison.OrdinalIgnoreCase)))
                continue;

            var scoreHistory = await scoreRepository.GetByPromptVersionIdAsync(candidate.PromptVersionId, cancellationToken);
            var latestScore = scoreHistory.Count == 0 ? null : scoreHistory[^1]; // chronological — last is most recent

            var repositoryCount = executions.Select(e => e.Context.Repository).Distinct(StringComparer.OrdinalIgnoreCase).Count();

            gathered.Add(new GatheredCandidate(candidate, matchedTagCount, latestScore?.OverallScore, latestScore?.SampleSize ?? 0, repositoryCount));
        }

        return gathered;
    }
}

internal sealed record GatheredCandidate(
    PromptRecommendationCandidate Candidate,
    int MatchedTagCount,
    double? Score,
    int SampleSize,
    int RepositoryCount);
