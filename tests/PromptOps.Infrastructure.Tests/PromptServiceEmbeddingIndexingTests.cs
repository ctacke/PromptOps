using PromptOps.Application.Embeddings;
using PromptOps.Application.Prompts;
using PromptOps.Application.Providers;
using PromptOps.Domain.Prompts;
using Xunit;

namespace PromptOps.Infrastructure.Tests;

/// <summary>
/// Pure unit tests (fakes, no SQLite) proving <see cref="PromptService"/> keeps its embedding index
/// up to date on the two events that change what a prompt version's embedded text represents: a new
/// version being created, and the prompt being re-tagged. SQLite round-tripping of the resulting
/// embeddings is already covered by <see cref="EmbeddingStoreTests"/> and
/// <c>PromptRepositoryIntegrationTests</c>; this class only cares about *when* and *with what text*
/// <see cref="PromptService"/> calls the embedding pipeline.
/// </summary>
public class PromptServiceEmbeddingIndexingTests
{
    [Fact]
    public async Task CreateVersionAsync_Indexes_An_Embedding_For_The_New_Version()
    {
        var repository = new FakeMutablePromptRepository();
        var embeddingProvider = new RecordingEmbeddingProvider();
        var embeddingStore = new RecordingEmbeddingStore();
        var service = new PromptService(repository, embeddingProvider, embeddingStore);

        var prompt = await service.CreatePromptAsync("Bug Triage", new PromptMetadata { Tags = ["debugging"] });
        var version = await service.CreateVersionAsync(prompt.Id, "Investigate the failure and propose a fix.", "alice");

        var call = Assert.Single(embeddingProvider.Calls);
        Assert.Equal("Bug Triage debugging Investigate the failure and propose a fix.", call);

        var stored = Assert.Single(embeddingStore.Stored);
        Assert.Equal(version.Id, stored.SubjectId);
        Assert.Equal(EmbeddingSubjectTypes.PromptVersion, stored.SubjectType);
    }

    [Fact]
    public async Task TagPromptAsync_Reindexes_The_Latest_Version_With_The_Updated_Tags()
    {
        var repository = new FakeMutablePromptRepository();
        var embeddingProvider = new RecordingEmbeddingProvider();
        var embeddingStore = new RecordingEmbeddingStore();
        var service = new PromptService(repository, embeddingProvider, embeddingStore);

        var prompt = await service.CreatePromptAsync("Bug Triage");
        var version = await service.CreateVersionAsync(prompt.Id, "Investigate the failure.", "alice");
        embeddingProvider.Calls.Clear();
        embeddingStore.Stored.Clear();

        await service.TagPromptAsync(prompt.Id, ["debugging", "csharp"]);

        var call = Assert.Single(embeddingProvider.Calls);
        Assert.Equal("Bug Triage debugging csharp Investigate the failure.", call);

        var stored = Assert.Single(embeddingStore.Stored);
        Assert.Equal(version.Id, stored.SubjectId);
        Assert.Equal(EmbeddingSubjectTypes.PromptVersion, stored.SubjectType);
    }

    [Fact]
    public async Task TagPromptAsync_On_A_Prompt_With_No_Versions_Does_Not_Touch_The_Embedding_Pipeline()
    {
        var repository = new FakeMutablePromptRepository();
        var embeddingProvider = new RecordingEmbeddingProvider();
        var embeddingStore = new RecordingEmbeddingStore();
        var service = new PromptService(repository, embeddingProvider, embeddingStore);

        var prompt = await service.CreatePromptAsync("Untouched");

        await service.TagPromptAsync(prompt.Id, ["debugging"]);

        Assert.Empty(embeddingProvider.Calls);
        Assert.Empty(embeddingStore.Stored);
    }

    private sealed class RecordingEmbeddingProvider : IEmbeddingProvider
    {
        public List<string> Calls { get; } = [];

        public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
        {
            Calls.Add(text);
            return Task.FromResult(new float[] { 1f });
        }
    }

    private sealed class RecordingEmbeddingStore : IEmbeddingStore
    {
        public List<(Guid SubjectId, string SubjectType, float[] Embedding)> Stored { get; } = [];

        public Task StoreAsync(Guid subjectId, string subjectType, float[] embedding, CancellationToken cancellationToken = default)
        {
            Stored.Add((subjectId, subjectType, embedding));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<EmbeddingMatch>> FindSimilarAsync(float[] queryEmbedding, string subjectType, int limit, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<EmbeddingMatch>>([]);
    }

}
