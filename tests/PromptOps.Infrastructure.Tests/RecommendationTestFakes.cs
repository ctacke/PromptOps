using PromptOps.Application.Executions;
using PromptOps.Application.Prompts;
using PromptOps.Application.Scoring;
using PromptOps.Domain.Executions;
using PromptOps.Domain.Prompts;
using PromptOps.Domain.Scoring;

namespace PromptOps.Infrastructure.Tests;

/// <summary>Shared by <see cref="TagAndHistoryRecommendationProviderTests"/> (v1) and <see cref="SemanticRecommendationProviderTests"/> (v2) — both exercise the same <see cref="RecommendationCandidateGatherer"/> underneath, so they need the same fakes.</summary>
internal sealed class FakePromptRepository : IPromptRepository
{
    private readonly List<PromptRecommendationCandidate> _candidates = [];

    public void Seed(PromptRecommendationCandidate candidate) => _candidates.Add(candidate);

    public Task AddAsync(Prompt prompt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<Prompt?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task UpdateAsync(Prompt prompt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<PromptMetadataView?> GetMetadataAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<IReadOnlyList<PromptRecommendationCandidate>> GetRecommendationCandidatesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<PromptRecommendationCandidate>>(_candidates);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

internal sealed class FakeExecutionRepository : IExecutionRepository
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

internal sealed class FakePromptScoreRepository : IPromptScoreRepository
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
