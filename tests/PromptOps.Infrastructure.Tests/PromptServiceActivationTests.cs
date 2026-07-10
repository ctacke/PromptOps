using PromptOps.Application.Embeddings;
using PromptOps.Application.Prompts;
using PromptOps.Application.Providers;
using PromptOps.Domain.Prompts;
using Xunit;

namespace PromptOps.Infrastructure.Tests;

/// <summary>Pure unit tests (fakes, no SQLite) for <see cref="PromptService.ActivateVersionAsync"/> — the manual activation primitive introduced in Phase 11, the counterpart to <c>AutoPromotionTrigger</c>'s automatic path.</summary>
public class PromptServiceActivationTests
{
    private static PromptService CreateService(FakeMutablePromptRepository repository)
        => new(repository, new NoopEmbeddingProvider(), new NoopEmbeddingStore());

    [Fact]
    public async Task ActivateVersionAsync_Activates_A_Draft_Version()
    {
        var repository = new FakeMutablePromptRepository();
        var service = CreateService(repository);
        var prompt = await service.CreatePromptAsync("Fix a bug");
        var version = await service.CreateVersionAsync(prompt.Id, "content", "alice");

        await service.ActivateVersionAsync(prompt.Id, version.Id);

        var reloaded = await repository.GetByIdAsync(prompt.Id);
        Assert.Equal(PromptVersionStatus.Active, reloaded!.Versions.Single().Status);
    }

    [Fact]
    public async Task ActivateVersionAsync_Deprecates_The_Previously_Active_Version()
    {
        var repository = new FakeMutablePromptRepository();
        var service = CreateService(repository);
        var prompt = await service.CreatePromptAsync("Fix a bug");
        var v1 = await service.CreateVersionAsync(prompt.Id, "first", "alice");
        var v2 = await service.CreateVersionAsync(prompt.Id, "second", "alice");
        await service.ActivateVersionAsync(prompt.Id, v1.Id);

        await service.ActivateVersionAsync(prompt.Id, v2.Id);

        var reloaded = await repository.GetByIdAsync(prompt.Id);
        Assert.Equal(PromptVersionStatus.Deprecated, reloaded!.Versions.Single(v => v.Id == v1.Id).Status);
        Assert.Equal(PromptVersionStatus.Active, reloaded.Versions.Single(v => v.Id == v2.Id).Status);
    }

    [Fact]
    public async Task ActivateVersionAsync_Throws_PromptNotFoundException_For_An_Unknown_Prompt()
    {
        var service = CreateService(new FakeMutablePromptRepository());

        await Assert.ThrowsAsync<PromptNotFoundException>(() => service.ActivateVersionAsync(Guid.NewGuid(), Guid.NewGuid()));
    }

    [Fact]
    public async Task ActivateVersionAsync_Throws_PromptVersionNotFoundException_For_An_Unknown_Version()
    {
        var repository = new FakeMutablePromptRepository();
        var service = CreateService(repository);
        var prompt = await service.CreatePromptAsync("Fix a bug");

        await Assert.ThrowsAsync<PromptVersionNotFoundException>(() => service.ActivateVersionAsync(prompt.Id, Guid.NewGuid()));
    }

    private sealed class NoopEmbeddingProvider : IEmbeddingProvider
    {
        public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
            => Task.FromResult(Array.Empty<float>());
    }

    private sealed class NoopEmbeddingStore : IEmbeddingStore
    {
        public Task StoreAsync(Guid subjectId, string subjectType, float[] embedding, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<EmbeddingMatch>> FindSimilarAsync(float[] queryEmbedding, string subjectType, int limit, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<EmbeddingMatch>>([]);
    }
}
