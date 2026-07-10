using PromptOps.Application.Prompts;
using PromptOps.Domain.Prompts;

namespace PromptOps.Infrastructure.Tests;

/// <summary>Shared by <see cref="PromptServiceEmbeddingIndexingTests"/>, <see cref="PromptServiceActivationTests"/>, and <see cref="AutoPromotionTriggerTests"/> — all three need a working (not <see cref="NotSupportedException"/>-stubbed) fake repository with real CRUD + version lookup, unlike <see cref="RecommendationTestFakes"/>'s read-only candidate fake.</summary>
internal sealed class FakeMutablePromptRepository : IPromptRepository
{
    private readonly Dictionary<Guid, Prompt> _prompts = [];

    public Task AddAsync(Prompt prompt, CancellationToken cancellationToken = default)
    {
        _prompts[prompt.Id] = prompt;
        return Task.CompletedTask;
    }

    public Task<Prompt?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => Task.FromResult(_prompts.GetValueOrDefault(id));

    public Task<Prompt?> GetByVersionIdAsync(Guid versionId, CancellationToken cancellationToken = default)
        => Task.FromResult(_prompts.Values.FirstOrDefault(p => p.Versions.Any(v => v.Id == versionId)));

    public Task UpdateAsync(Prompt prompt, CancellationToken cancellationToken = default)
    {
        _prompts[prompt.Id] = prompt;
        return Task.CompletedTask;
    }

    public Task<PromptMetadataView?> GetMetadataAsync(Guid id, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<IReadOnlyList<PromptRecommendationCandidate>> GetRecommendationCandidatesAsync(CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<IReadOnlyList<PromptSummary>> GetAllNamesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<PromptSummary>>(_prompts.Values.Select(p => new PromptSummary(p.Id, p.Name)).ToList());

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
