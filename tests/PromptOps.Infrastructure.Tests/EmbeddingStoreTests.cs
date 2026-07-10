using PromptOps.Infrastructure.Persistence;
using Xunit;

namespace PromptOps.Infrastructure.Tests;

public class EmbeddingStoreTests(SqliteFixture fixture) : IClassFixture<SqliteFixture>
{
    // The fixture's SQLite file is shared across every [Fact] in this class, and FindSimilarAsync
    // scans *all* rows of a given subjectType — so each test gets its own unique subjectType to stay
    // isolated from the others, rather than sharing one constant.
    private static string NewSubjectType() => $"PromptVersion-{Guid.NewGuid():N}";

    [Fact]
    public async Task Stores_And_Finds_The_Most_Similar_Embedding_First()
    {
        var subjectType = NewSubjectType();
        var closeId = Guid.NewGuid();
        var farId = Guid.NewGuid();

        using (var dbContext = fixture.CreateContext())
        {
            var store = new EmbeddingStore(dbContext);
            await store.StoreAsync(closeId, subjectType, [1f, 0f, 0f]);
            await store.StoreAsync(farId, subjectType, [0f, 1f, 0f]);
        }

        // Reload through a brand new context to prove this round-tripped through SQLite.
        using var freshContext = fixture.CreateContext();
        var freshStore = new EmbeddingStore(freshContext);
        var matches = await freshStore.FindSimilarAsync([1f, 0f, 0f], subjectType, limit: 5);

        Assert.Equal(2, matches.Count);
        Assert.Equal(closeId, matches[0].SubjectId);
        Assert.Equal(1.0, matches[0].Similarity, precision: 5);
        Assert.Equal(farId, matches[1].SubjectId);
        Assert.Equal(0.0, matches[1].Similarity, precision: 5);
    }

    [Fact]
    public async Task StoreAsync_Upserts_Rather_Than_Duplicating_For_The_Same_Subject()
    {
        var subjectType = NewSubjectType();
        var subjectId = Guid.NewGuid();

        using (var dbContext = fixture.CreateContext())
        {
            var store = new EmbeddingStore(dbContext);
            await store.StoreAsync(subjectId, subjectType, [1f, 0f, 0f]);
            await store.StoreAsync(subjectId, subjectType, [0f, 1f, 0f]); // re-index, e.g. after re-tagging
        }

        using var freshContext = fixture.CreateContext();
        var freshStore = new EmbeddingStore(freshContext);
        var matches = await freshStore.FindSimilarAsync([0f, 1f, 0f], subjectType, limit: 10);

        var match = Assert.Single(matches);
        Assert.Equal(subjectId, match.SubjectId);
        Assert.Equal(1.0, match.Similarity, precision: 5);
    }

    [Fact]
    public async Task FindSimilarAsync_Only_Returns_Embeddings_Of_The_Requested_SubjectType()
    {
        var otherSubjectType = NewSubjectType();
        var requestedSubjectType = NewSubjectType();

        using (var dbContext = fixture.CreateContext())
        {
            var store = new EmbeddingStore(dbContext);
            await store.StoreAsync(Guid.NewGuid(), otherSubjectType, [1f, 0f, 0f]);
        }

        using var freshContext = fixture.CreateContext();
        var freshStore = new EmbeddingStore(freshContext);
        var matches = await freshStore.FindSimilarAsync([1f, 0f, 0f], requestedSubjectType, limit: 10);

        Assert.Empty(matches);
    }

    [Fact]
    public async Task FindSimilarAsync_Respects_The_Limit()
    {
        var subjectType = NewSubjectType();

        using (var dbContext = fixture.CreateContext())
        {
            var store = new EmbeddingStore(dbContext);
            for (var i = 0; i < 5; i++)
                await store.StoreAsync(Guid.NewGuid(), subjectType, [1f, 0f, 0f]);
        }

        using var freshContext = fixture.CreateContext();
        var freshStore = new EmbeddingStore(freshContext);
        var matches = await freshStore.FindSimilarAsync([1f, 0f, 0f], subjectType, limit: 2);

        Assert.Equal(2, matches.Count);
    }
}
