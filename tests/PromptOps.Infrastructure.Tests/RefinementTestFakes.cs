using PromptOps.Application.Embeddings;
using PromptOps.Application.Evaluations;
using PromptOps.Application.Executions;
using PromptOps.Application.Providers;
using PromptOps.Application.Refinement;
using PromptOps.Domain.Evaluations;
using PromptOps.Domain.Executions;
using PromptOps.Domain.Refinement;

namespace PromptOps.Infrastructure.Tests;

/// <summary>Shared by <see cref="PromptRefinementServiceTests"/> and <see cref="PromptRefinementTriggerTests"/> (Phase 16a) — seedable execution/evaluation repositories, a controllable refiner, a policy fake, and no-op embedding fakes.</summary>
internal sealed class FakeSeededExecutionRepository : IExecutionRepository
{
    private readonly Dictionary<Guid, ExecutionRecord> _byId = [];
    public void Seed(ExecutionRecord execution) => _byId[execution.Id] = execution;

    public Task<ExecutionRecord?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => Task.FromResult(_byId.GetValueOrDefault(id));

    public Task AddAsync(ExecutionRecord execution, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<IReadOnlyList<ExecutionRecord>> GetByPromptVersionIdAsync(Guid promptVersionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task UpdateAsync(ExecutionRecord execution, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<ExecutionStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

internal sealed class FakeSeededAIEvaluationRepository : IAIEvaluationRepository
{
    private readonly List<AIEvaluation> _evaluations = [];
    public void Seed(AIEvaluation evaluation) => _evaluations.Add(evaluation);

    public Task<IReadOnlyList<AIEvaluation>> GetByExecutionIdAsync(Guid executionId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<AIEvaluation>>(_evaluations.Where(e => e.ExecutionId == executionId).ToList());

    public Task AddAsync(AIEvaluation evaluation, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<int> GetCountAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

internal sealed class FakeRefinementPolicyRepository(RefinementPolicy policy) : IRefinementPolicyRepository
{
    public Task<RefinementPolicy?> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult<RefinementPolicy?>(policy);
    public Task AddAsync(RefinementPolicy policy, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task UpdateAsync(RefinementPolicy policy, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

internal sealed class FakeRefiner : IPromptRefinementProvider
{
    public string? Result { get; set; }
    public int CallCount { get; private set; }
    public bool ShouldThrow { get; set; }
    public TaskCompletionSource Invoked { get; } = new();

    public Task<string> RefineAsync(string currentContent, IReadOnlyList<string> suggestions, CancellationToken cancellationToken = default)
    {
        CallCount++;
        Invoked.TrySetResult();
        if (ShouldThrow)
            throw new InvalidOperationException("simulated refiner failure");
        return Task.FromResult(Result ?? currentContent + " (improved)");
    }
}

internal sealed class FakeRefinementCandidateRepository : IRefinementCandidateRepository
{
    public List<RefinementCandidate> Candidates { get; } = [];

    public Task AddAsync(RefinementCandidate candidate, CancellationToken cancellationToken = default)
    {
        Candidates.Add(candidate);
        return Task.CompletedTask;
    }

    public Task<RefinementCandidate?> GetByDraftVersionIdAsync(Guid draftVersionId, CancellationToken cancellationToken = default)
        => Task.FromResult(Candidates.FirstOrDefault(c => c.DraftVersionId == draftVersionId));

    public Task<IReadOnlyList<RefinementCandidate>> GetAbEligibleByActiveVersionIdAsync(Guid activeVersionId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<RefinementCandidate>>(
            Candidates.Where(c => c.ActiveVersionId == activeVersionId && c.Status == RefinementCandidateStatus.AbEligible).ToList());

    public Task UpdateAsync(RefinementCandidate candidate, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

internal sealed class FakeBenchmarkProvider : IPromptBenchmarkProvider
{
    public BenchmarkComparison? Result { get; set; }
    public int CallCount { get; private set; }

    public Task<BenchmarkComparison?> CompareAsync(string activeContent, string candidateContent, IReadOnlyList<string> tags, int sampleSize, CancellationToken cancellationToken = default)
    {
        CallCount++;
        return Task.FromResult(Result);
    }
}

internal sealed class FakeExplorationSampler(bool shouldExplore) : IExplorationSampler
{
    public double LastRate { get; private set; } = double.NaN;

    public bool ShouldExplore(double rate)
    {
        LastRate = rate;
        return shouldExplore;
    }
}

internal sealed class NoopEmbeddingProvider : IEmbeddingProvider
{
    public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default) => Task.FromResult(Array.Empty<float>());
}

internal sealed class NoopEmbeddingStore : IEmbeddingStore
{
    public Task StoreAsync(Guid subjectId, string subjectType, float[] embedding, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<IReadOnlyList<EmbeddingMatch>> FindSimilarAsync(float[] queryEmbedding, string subjectType, int limit, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<EmbeddingMatch>>([]);
}
