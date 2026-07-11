using PromptOps.Application.Embeddings;
using PromptOps.Application.Prompts;
using PromptOps.Application.Providers;
using PromptOps.Domain.Prompts;
using Xunit;

namespace PromptOps.Infrastructure.Tests;

/// <summary>Pure unit tests (fakes, no SQLite) for <see cref="PromptService.GetVersionDetailAsync"/> — the content-lookup primitive that makes "show me the current prompt for X" answerable, since nothing else returns a version's Content except at creation time.</summary>
public class PromptServiceVersionDetailTests
{
    private static PromptService CreateService(FakeMutablePromptRepository repository)
        => new(repository, new NoopEmbeddingProvider(), new NoopEmbeddingStore());

    [Fact]
    public async Task GetVersionDetailAsync_Returns_The_Versions_Content_And_Owning_Prompts_Identity()
    {
        var repository = new FakeMutablePromptRepository();
        var service = CreateService(repository);
        var prompt = await service.CreatePromptAsync("Fix a Bug", new PromptMetadata { Tags = ["debugging"] });
        var version = await service.CreateVersionAsync(prompt.Id, "Investigate and fix the bug.", "alice");

        var detail = await service.GetVersionDetailAsync(version.Id);

        Assert.NotNull(detail);
        Assert.Equal(prompt.Id, detail!.PromptId);
        Assert.Equal("Fix a Bug", detail.PromptName);
        Assert.Equal(version.Id, detail.VersionId);
        Assert.Equal(1, detail.VersionNumber);
        Assert.Equal("Investigate and fix the bug.", detail.Content);
        Assert.Equal(PromptVersionStatus.Draft, detail.Status);
        Assert.Contains("debugging", detail.Tags);
    }

    [Fact]
    public async Task GetVersionDetailAsync_Reflects_Activation_Status()
    {
        var repository = new FakeMutablePromptRepository();
        var service = CreateService(repository);
        var prompt = await service.CreatePromptAsync("Fix a Bug");
        var version = await service.CreateVersionAsync(prompt.Id, "content", "alice");
        await service.ActivateVersionAsync(prompt.Id, version.Id);

        var detail = await service.GetVersionDetailAsync(version.Id);

        Assert.Equal(PromptVersionStatus.Active, detail!.Status);
    }

    [Fact]
    public async Task GetVersionDetailAsync_Returns_Null_For_An_Unknown_Version()
    {
        var service = CreateService(new FakeMutablePromptRepository());

        var detail = await service.GetVersionDetailAsync(Guid.NewGuid());

        Assert.Null(detail);
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
