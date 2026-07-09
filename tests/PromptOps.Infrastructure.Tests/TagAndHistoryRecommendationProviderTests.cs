using PromptOps.Application.Executions;
using PromptOps.Application.Prompts;
using PromptOps.Application.Scoring;
using PromptOps.Domain.Executions;
using PromptOps.Domain.Scoring;
using PromptOps.Infrastructure.Providers;
using Xunit;

namespace PromptOps.Infrastructure.Tests;

/// <summary>Pure unit tests — no SQLite, same shape as WeightedSumScoringProviderTests. Seeded multi-repo execution/score history, per the phase's explicit testing requirement.</summary>
public class TagAndHistoryRecommendationProviderTests
{
    private static ExecutionRecord FinishedExecution(Guid promptVersionId, string repository)
    {
        var execution = ExecutionRecord.Start(promptVersionId, "alice", new DevelopmentContext { Repository = repository });
        execution.Finish("output", TimeSpan.FromSeconds(1), "manual", null, null, [], 0, 0);
        return execution;
    }

    [Fact]
    public async Task Only_Returns_Candidates_Whose_Tags_Overlap_The_Requested_Tags()
    {
        var prompts = new FakePromptRepository();
        var debuggingVersionId = Guid.NewGuid();
        var testingVersionId = Guid.NewGuid();
        prompts.Seed(new PromptRecommendationCandidate(Guid.NewGuid(), "Debug Helper", ["debugging", "csharp"], debuggingVersionId, 1));
        prompts.Seed(new PromptRecommendationCandidate(Guid.NewGuid(), "Test Writer", ["testing"], testingVersionId, 1));

        var executions = new FakeExecutionRepository();
        executions.Seed(debuggingVersionId, FinishedExecution(debuggingVersionId, "repo-a"));
        executions.Seed(testingVersionId, FinishedExecution(testingVersionId, "repo-a"));

        var provider = new TagAndHistoryRecommendationProvider(prompts, new FakePromptScoreRepository(), executions);

        var results = await provider.RecommendAsync(["debugging"]);

        var result = Assert.Single(results);
        Assert.Equal(debuggingVersionId, result.RecommendedPromptVersionId);
    }

    [Fact]
    public async Task Empty_Tags_Means_No_Filter_Everything_Scored_Is_Returned()
    {
        var prompts = new FakePromptRepository();
        var versionA = Guid.NewGuid();
        var versionB = Guid.NewGuid();
        prompts.Seed(new PromptRecommendationCandidate(Guid.NewGuid(), "A", ["debugging"], versionA, 1));
        prompts.Seed(new PromptRecommendationCandidate(Guid.NewGuid(), "B", ["testing"], versionB, 1));

        var executions = new FakeExecutionRepository();
        executions.Seed(versionA, FinishedExecution(versionA, "repo-a"));
        executions.Seed(versionB, FinishedExecution(versionB, "repo-a"));

        var provider = new TagAndHistoryRecommendationProvider(prompts, new FakePromptScoreRepository(), executions);

        var results = await provider.RecommendAsync([]);

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task A_Brand_New_Repo_Still_Gets_A_Recommendation_Proven_Only_In_Another_Repo()
    {
        var prompts = new FakePromptRepository();
        var versionId = Guid.NewGuid();
        prompts.Seed(new PromptRecommendationCandidate(Guid.NewGuid(), "Debug Helper", ["debugging"], versionId, 1));

        var executions = new FakeExecutionRepository();
        var execution = FinishedExecution(versionId, "some-other-repo");
        executions.Seed(versionId, execution);

        var scores = new FakePromptScoreRepository();
        scores.Seed(versionId, PromptScore.Compute(versionId, Guid.NewGuid(), 92.0, new Dictionary<string, double> { ["humanRating"] = 92.0 }, 1));

        var provider = new TagAndHistoryRecommendationProvider(prompts, scores, executions);

        // No repository filter — this is the call a brand-new repo's session makes; it has no
        // history of its own, but the prompt's only execution history is in a *different* repo.
        var results = await provider.RecommendAsync(["debugging"]);

        var result = Assert.Single(results);
        Assert.Equal(versionId, result.RecommendedPromptVersionId);
        Assert.Contains("Score: 92.0/100", result.Rationale);
        Assert.Contains("1 repo(s)", result.Rationale);
    }

    [Fact]
    public async Task Repository_Filter_Excludes_Candidates_With_No_Execution_History_In_That_Repo()
    {
        var prompts = new FakePromptRepository();
        var versionId = Guid.NewGuid();
        prompts.Seed(new PromptRecommendationCandidate(Guid.NewGuid(), "Debug Helper", ["debugging"], versionId, 1));

        var executions = new FakeExecutionRepository();
        executions.Seed(versionId, FinishedExecution(versionId, "some-other-repo"));

        var provider = new TagAndHistoryRecommendationProvider(prompts, new FakePromptScoreRepository(), executions);

        var results = await provider.RecommendAsync(["debugging"], repository: "this-repo");

        Assert.Empty(results);
    }

    [Fact]
    public async Task Repository_Filter_Includes_Candidates_With_Matching_Execution_History()
    {
        var prompts = new FakePromptRepository();
        var versionId = Guid.NewGuid();
        prompts.Seed(new PromptRecommendationCandidate(Guid.NewGuid(), "Debug Helper", ["debugging"], versionId, 1));

        var executions = new FakeExecutionRepository();
        executions.Seed(versionId, FinishedExecution(versionId, "this-repo"));

        var provider = new TagAndHistoryRecommendationProvider(prompts, new FakePromptScoreRepository(), executions);

        var results = await provider.RecommendAsync(["debugging"], repository: "this-repo");

        Assert.Single(results);
    }

    [Fact]
    public async Task Scored_Candidates_Rank_Above_Never_Scored_Candidates_Regardless_Of_Tag_Match_Count()
    {
        var prompts = new FakePromptRepository();
        var scoredVersion = Guid.NewGuid();
        var unscoredVersion = Guid.NewGuid();
        // Unscored candidate matches both tags; scored candidate matches only one — still must rank first.
        prompts.Seed(new PromptRecommendationCandidate(Guid.NewGuid(), "Scored", ["debugging"], scoredVersion, 1));
        prompts.Seed(new PromptRecommendationCandidate(Guid.NewGuid(), "Unscored", ["debugging", "csharp"], unscoredVersion, 1));

        var executions = new FakeExecutionRepository();
        executions.Seed(scoredVersion, FinishedExecution(scoredVersion, "repo-a"));
        executions.Seed(unscoredVersion, FinishedExecution(unscoredVersion, "repo-a"));

        var scores = new FakePromptScoreRepository();
        scores.Seed(scoredVersion, PromptScore.Compute(scoredVersion, Guid.NewGuid(), 60.0, new Dictionary<string, double>(), 1));

        var provider = new TagAndHistoryRecommendationProvider(prompts, scores, executions);

        var results = await provider.RecommendAsync(["debugging", "csharp"]);

        Assert.Equal(2, results.Count);
        Assert.Equal(scoredVersion, results[0].RecommendedPromptVersionId);
        Assert.Equal(1, results[0].Rank);
        Assert.Equal(unscoredVersion, results[1].RecommendedPromptVersionId);
        Assert.Contains("Not yet scored", results[1].Rationale);
    }

    [Fact]
    public async Task Higher_Score_Ranks_Above_Lower_Score()
    {
        var prompts = new FakePromptRepository();
        var lowVersion = Guid.NewGuid();
        var highVersion = Guid.NewGuid();
        prompts.Seed(new PromptRecommendationCandidate(Guid.NewGuid(), "Low", ["debugging"], lowVersion, 1));
        prompts.Seed(new PromptRecommendationCandidate(Guid.NewGuid(), "High", ["debugging"], highVersion, 1));

        var executions = new FakeExecutionRepository();
        executions.Seed(lowVersion, FinishedExecution(lowVersion, "repo-a"));
        executions.Seed(highVersion, FinishedExecution(highVersion, "repo-a"));

        var scores = new FakePromptScoreRepository();
        scores.Seed(lowVersion, PromptScore.Compute(lowVersion, Guid.NewGuid(), 40.0, new Dictionary<string, double>(), 1));
        scores.Seed(highVersion, PromptScore.Compute(highVersion, Guid.NewGuid(), 85.0, new Dictionary<string, double>(), 1));

        var provider = new TagAndHistoryRecommendationProvider(prompts, scores, executions);

        var results = await provider.RecommendAsync(["debugging"]);

        Assert.Equal(highVersion, results[0].RecommendedPromptVersionId);
        Assert.Equal(lowVersion, results[1].RecommendedPromptVersionId);
    }

    [Fact]
    public async Task Uses_The_Most_Recent_Score_When_A_PromptVersion_Has_Been_Rescored()
    {
        var prompts = new FakePromptRepository();
        var versionId = Guid.NewGuid();
        prompts.Seed(new PromptRecommendationCandidate(Guid.NewGuid(), "A", ["debugging"], versionId, 1));

        var executions = new FakeExecutionRepository();
        executions.Seed(versionId, FinishedExecution(versionId, "repo-a"));

        var scores = new FakePromptScoreRepository();
        scores.Seed(versionId, PromptScore.Compute(versionId, Guid.NewGuid(), 40.0, new Dictionary<string, double>(), 1, computedAt: DateTimeOffset.UtcNow.AddHours(-1)));
        scores.Seed(versionId, PromptScore.Compute(versionId, Guid.NewGuid(), 90.0, new Dictionary<string, double>(), 2, computedAt: DateTimeOffset.UtcNow));

        var provider = new TagAndHistoryRecommendationProvider(prompts, scores, executions);

        var results = await provider.RecommendAsync(["debugging"]);

        Assert.Contains("Score: 90.0/100", results[0].Rationale);
    }

    [Fact]
    public async Task Limit_Caps_The_Number_Of_Results()
    {
        var prompts = new FakePromptRepository();
        var executions = new FakeExecutionRepository();
        var scores = new FakePromptScoreRepository();

        for (var i = 0; i < 5; i++)
        {
            var versionId = Guid.NewGuid();
            prompts.Seed(new PromptRecommendationCandidate(Guid.NewGuid(), $"Prompt{i}", ["debugging"], versionId, 1));
            executions.Seed(versionId, FinishedExecution(versionId, "repo-a"));
            scores.Seed(versionId, PromptScore.Compute(versionId, Guid.NewGuid(), i * 10.0, new Dictionary<string, double>(), 1));
        }

        var provider = new TagAndHistoryRecommendationProvider(prompts, scores, executions);

        var results = await provider.RecommendAsync(["debugging"], limit: 2);

        Assert.Equal(2, results.Count);
    }

    private sealed class FakePromptRepository : IPromptRepository
    {
        private readonly List<PromptRecommendationCandidate> _candidates = [];

        public void Seed(PromptRecommendationCandidate candidate) => _candidates.Add(candidate);

        public Task AddAsync(Domain.Prompts.Prompt prompt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Domain.Prompts.Prompt?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateAsync(Domain.Prompts.Prompt prompt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PromptMetadataView?> GetMetadataAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<PromptRecommendationCandidate>> GetRecommendationCandidatesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<PromptRecommendationCandidate>>(_candidates);

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeExecutionRepository : IExecutionRepository
    {
        private readonly Dictionary<Guid, List<ExecutionRecord>> _byPromptVersion = [];

        public void Seed(Guid promptVersionId, ExecutionRecord execution)
        {
            if (!_byPromptVersion.TryGetValue(promptVersionId, out var list))
                _byPromptVersion[promptVersionId] = list = [];
            list.Add(execution);
        }

        public Task AddAsync(ExecutionRecord execution, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<ExecutionRecord?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<ExecutionRecord?>(null);

        public Task<IReadOnlyList<ExecutionRecord>> GetByPromptVersionIdAsync(Guid promptVersionId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ExecutionRecord>>(_byPromptVersion.GetValueOrDefault(promptVersionId) ?? []);

        public Task UpdateAsync(ExecutionRecord execution, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakePromptScoreRepository : IPromptScoreRepository
    {
        private readonly Dictionary<Guid, List<PromptScore>> _byPromptVersion = [];

        public void Seed(Guid promptVersionId, PromptScore score)
        {
            if (!_byPromptVersion.TryGetValue(promptVersionId, out var list))
                _byPromptVersion[promptVersionId] = list = [];
            list.Add(score);
        }

        public Task AddAsync(PromptScore score, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<PromptScore>> GetByPromptVersionIdAsync(Guid promptVersionId, CancellationToken cancellationToken = default)
        {
            var scores = _byPromptVersion.GetValueOrDefault(promptVersionId) ?? [];
            return Task.FromResult<IReadOnlyList<PromptScore>>(scores.OrderBy(s => s.ComputedAt).ToList());
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
