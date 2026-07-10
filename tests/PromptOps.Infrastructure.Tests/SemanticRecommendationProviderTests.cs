using PromptOps.Application.Embeddings;
using PromptOps.Application.Prompts;
using PromptOps.Application.Providers;
using PromptOps.Application.Scoring;
using PromptOps.Domain.Executions;
using PromptOps.Domain.Scoring;
using PromptOps.Infrastructure.Providers;
using Xunit;

namespace PromptOps.Infrastructure.Tests;

/// <summary>
/// Pure unit tests against fixture (fixed, hand-picked) embedding vectors — the phase's explicit
/// testing note ("integration tests with fixture embeddings") — rather than the real hashing
/// provider, so similarity values are exact and the test intent is legible. Real-embedding-provider
/// coverage lives in <see cref="HashingBagOfWordsEmbeddingProviderTests"/> and the Host-level
/// <c>RecommendationEndpointsTests</c> (which exercises the real provider end-to-end over HTTP).
/// </summary>
public class SemanticRecommendationProviderTests
{
    private static ExecutionRecord FinishedExecution(Guid promptVersionId, string repository)
    {
        var execution = ExecutionRecord.Start(promptVersionId, "alice", new DevelopmentContext { Repository = repository });
        execution.Finish("output", TimeSpan.FromSeconds(1), "manual", null, null, [], 0, 0);
        return execution;
    }

    [Fact]
    public async Task A_Semantically_Similar_Candidate_Surfaces_And_Outranks_An_Unrelated_One_Despite_Zero_Tag_Overlap()
    {
        var prompts = new FakePromptRepository();
        var relevantId = Guid.NewGuid();
        var unrelatedId = Guid.NewGuid();
        // Neither candidate's tags match the requested tag at all.
        prompts.Seed(new PromptRecommendationCandidate(Guid.NewGuid(), "Relevant", ["some-other-tag"], relevantId, 1));
        prompts.Seed(new PromptRecommendationCandidate(Guid.NewGuid(), "Unrelated", ["another-tag"], unrelatedId, 1));

        var executions = new FakeExecutionRepository();
        executions.Seed(relevantId, FinishedExecution(relevantId, "repo-a"));
        executions.Seed(unrelatedId, FinishedExecution(unrelatedId, "repo-a"));

        var embeddingStore = new FakeEmbeddingStore();
        embeddingStore.Seed(relevantId, [1f, 0f]);
        embeddingStore.Seed(unrelatedId, [0f, 1f]);
        var embeddingProvider = new FakeEmbeddingProvider(new() { ["debugging task"] = [1f, 0f] });

        var provider = new SemanticRecommendationProvider(prompts, new FakePromptScoreRepository(), executions, embeddingProvider, embeddingStore);

        var results = await provider.RecommendAsync(["completely-different-tag"], taskDescription: "debugging task");

        Assert.Equal(2, results.Count);
        Assert.Equal(relevantId, results[0].RecommendedPromptVersionId);
        Assert.Contains("Semantic similarity: 1.00", results[0].Rationale);
        Assert.Contains("Matched 0/1 requested tag(s)", results[0].Rationale);
    }

    [Fact]
    public async Task A_Missing_Component_Is_Excluded_From_The_Blend_Not_Scored_As_Zero()
    {
        // No tags requested — the tag component is globally excluded for this call, isolating the
        // interaction between the score and semantic components specifically.
        var prompts = new FakePromptRepository();
        var scoredOnlyId = Guid.NewGuid();
        var embeddedOnlyId = Guid.NewGuid();
        prompts.Seed(new PromptRecommendationCandidate(Guid.NewGuid(), "ScoredOnly", [], scoredOnlyId, 1));
        prompts.Seed(new PromptRecommendationCandidate(Guid.NewGuid(), "EmbeddedOnly", [], embeddedOnlyId, 1));

        var executions = new FakeExecutionRepository();
        executions.Seed(scoredOnlyId, FinishedExecution(scoredOnlyId, "repo-a"));
        executions.Seed(embeddedOnlyId, FinishedExecution(embeddedOnlyId, "repo-a"));

        var scores = new FakePromptScoreRepository();
        scores.Seed(scoredOnlyId, PromptScore.Compute(scoredOnlyId, Guid.NewGuid(), 90.0, new Dictionary<string, double>(), 1)); // no embedding

        var embeddingStore = new FakeEmbeddingStore();
        embeddingStore.Seed(embeddedOnlyId, [1f, 0f]); // no score
        var embeddingProvider = new FakeEmbeddingProvider(new() { ["query"] = [0.7f, 0.714143f] }); // cosine ≈ 0.7 against [1,0]

        var provider = new SemanticRecommendationProvider(prompts, scores, executions, embeddingProvider, embeddingStore);

        var results = await provider.RecommendAsync([], taskDescription: "query");

        // Correct (exclude-missing) math: ScoredOnly blend = 0.9 (its only component, unweighted-down
        // by the other components it doesn't have). EmbeddedOnly blend ≈ 0.7 (ditto). ScoredOnly wins.
        // A naive "missing = 0, always divide by full weight" implementation would produce the
        // opposite order (0.27 vs 0.28) — see this test's design notes in the PR/commit, this exact
        // pair of numbers was chosen to flip order between the two implementations.
        Assert.Equal(scoredOnlyId, results[0].RecommendedPromptVersionId);
        Assert.Equal(embeddedOnlyId, results[1].RecommendedPromptVersionId);
    }

    [Fact]
    public async Task No_Query_Text_At_All_Skips_The_Embedding_Call_Entirely()
    {
        var prompts = new FakePromptRepository();
        var versionId = Guid.NewGuid();
        prompts.Seed(new PromptRecommendationCandidate(Guid.NewGuid(), "A", [], versionId, 1));

        var executions = new FakeExecutionRepository();
        executions.Seed(versionId, FinishedExecution(versionId, "repo-a"));

        var embeddingProvider = new FakeEmbeddingProvider(new());
        var provider = new SemanticRecommendationProvider(prompts, new FakePromptScoreRepository(), executions, embeddingProvider, new FakeEmbeddingStore());

        var results = await provider.RecommendAsync([], taskDescription: null);

        Assert.Equal(0, embeddingProvider.CallCount);
        Assert.Contains("No semantic signal available", results[0].Rationale);
    }

    [Fact]
    public async Task Falls_Back_To_Joined_Tags_As_Query_Text_When_No_TaskDescription_Given()
    {
        var prompts = new FakePromptRepository();
        var versionId = Guid.NewGuid();
        prompts.Seed(new PromptRecommendationCandidate(Guid.NewGuid(), "A", [], versionId, 1));

        var executions = new FakeExecutionRepository();
        executions.Seed(versionId, FinishedExecution(versionId, "repo-a"));

        var embeddingStore = new FakeEmbeddingStore();
        embeddingStore.Seed(versionId, [1f, 0f]);
        var embeddingProvider = new FakeEmbeddingProvider(new() { ["debugging performance"] = [1f, 0f] });

        var provider = new SemanticRecommendationProvider(prompts, new FakePromptScoreRepository(), executions, embeddingProvider, embeddingStore);

        var results = await provider.RecommendAsync(["debugging", "performance"], taskDescription: null);

        Assert.Equal(1, embeddingProvider.CallCount);
        Assert.Contains("Semantic similarity: 1.00", results[0].Rationale);
    }

    [Fact]
    public async Task Repository_Filter_Still_Applies()
    {
        var prompts = new FakePromptRepository();
        var versionId = Guid.NewGuid();
        prompts.Seed(new PromptRecommendationCandidate(Guid.NewGuid(), "A", [], versionId, 1));

        var executions = new FakeExecutionRepository();
        executions.Seed(versionId, FinishedExecution(versionId, "some-other-repo"));

        var provider = new SemanticRecommendationProvider(
            prompts, new FakePromptScoreRepository(), executions, new FakeEmbeddingProvider(new()), new FakeEmbeddingStore());

        var results = await provider.RecommendAsync([], repository: "this-repo");

        Assert.Empty(results);
    }

    private sealed class FakeEmbeddingProvider(Dictionary<string, float[]> embeddings) : IEmbeddingProvider
    {
        public int CallCount { get; private set; }

        public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(embeddings.TryGetValue(text, out var vector) ? vector : new float[2]);
        }
    }

    private sealed class FakeEmbeddingStore : IEmbeddingStore
    {
        private readonly Dictionary<Guid, float[]> _vectors = [];

        public void Seed(Guid subjectId, float[] vector) => _vectors[subjectId] = vector;

        public Task StoreAsync(Guid subjectId, string subjectType, float[] embedding, CancellationToken cancellationToken = default)
        {
            _vectors[subjectId] = embedding;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<EmbeddingMatch>> FindSimilarAsync(float[] queryEmbedding, string subjectType, int limit, CancellationToken cancellationToken = default)
        {
            var matches = _vectors
                .Select(kv => new EmbeddingMatch(kv.Key, CosineSimilarity(queryEmbedding, kv.Value)))
                .OrderByDescending(m => m.Similarity)
                .Take(limit)
                .ToList();
            return Task.FromResult<IReadOnlyList<EmbeddingMatch>>(matches);
        }

        private static double CosineSimilarity(float[] a, float[] b)
        {
            double dot = 0, magnitudeA = 0, magnitudeB = 0;
            for (var i = 0; i < a.Length; i++)
            {
                dot += a[i] * b[i];
                magnitudeA += a[i] * a[i];
                magnitudeB += b[i] * b[i];
            }
            if (magnitudeA <= 0 || magnitudeB <= 0) return 0.0;
            return dot / (Math.Sqrt(magnitudeA) * Math.Sqrt(magnitudeB));
        }
    }
}
