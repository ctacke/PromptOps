using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PromptOps.Application.Prompts;
using PromptOps.Domain.Prompts;
using PromptOps.Infrastructure.Persistence;
using PromptOps.Infrastructure.Providers;
using Xunit;

namespace PromptOps.Infrastructure.Tests;

public class PromptRepositoryIntegrationTests(SqliteFixture fixture) : IClassFixture<SqliteFixture>
{
    [Fact]
    public async Task Create_prompt_add_versions_and_retrieve_full_history_and_changelog()
    {
        Guid promptId;
        using (var context = fixture.CreateContext())
        {
            var service = new PromptService(new PromptRepository(context), new HashingBagOfWordsEmbeddingProvider(), new EmbeddingStore(context));
            var prompt = await service.CreatePromptAsync("Fix a bug", new PromptMetadata { Tags = ["bugfix"] });
            promptId = prompt.Id;

            await service.CreateVersionAsync(promptId, "v1 content", "alice", "initial draft");
            await service.CreateVersionAsync(promptId, "v2 content", "alice", "tightened wording");
        }

        // Reload through a brand new context to prove this round-tripped through SQLite, not just in-memory tracking.
        using var freshContext = fixture.CreateContext();
        var reloaded = await new PromptRepository(freshContext).GetByIdAsync(promptId);

        Assert.NotNull(reloaded);
        Assert.Equal(2, reloaded!.Versions.Count);

        Assert.Equal(1, reloaded.Versions[0].VersionNumber);
        Assert.Equal("v1 content", reloaded.Versions[0].Content);
        Assert.Equal("initial draft", reloaded.Versions[0].ChangelogEntry);
        Assert.Null(reloaded.Versions[0].ParentVersionId);

        Assert.Equal(2, reloaded.Versions[1].VersionNumber);
        Assert.Equal("v2 content", reloaded.Versions[1].Content);
        Assert.Equal("tightened wording", reloaded.Versions[1].ChangelogEntry);
        Assert.Equal(reloaded.Versions[0].Id, reloaded.Versions[1].ParentVersionId);
    }

    [Fact]
    public async Task Metadata_is_queryable_without_the_query_touching_version_content()
    {
        Guid promptId;
        using (var setupContext = fixture.CreateContext())
        {
            var service = new PromptService(new PromptRepository(setupContext), new HashingBagOfWordsEmbeddingProvider(), new EmbeddingStore(setupContext));
            var prompt = await service.CreatePromptAsync(
                "Fix a bug",
                new PromptMetadata { Tags = ["bugfix"], Owners = ["alice"], Description = "desc" });
            promptId = prompt.Id;

            await service.CreateVersionAsync(promptId, "some long content that must not be touched", "alice");
        }

        var capturedSql = new List<string>();
        using var queryContext = fixture.CreateContext(builder =>
            builder.LogTo(sql => capturedSql.Add(sql), [DbLoggerCategory.Database.Command.Name], LogLevel.Information));

        var metadata = await new PromptRepository(queryContext).GetMetadataAsync(promptId);

        Assert.NotNull(metadata);
        Assert.Equal("Fix a bug", metadata!.Name);
        Assert.Contains("bugfix", metadata.Metadata.Tags);
        Assert.Contains("alice", metadata.Metadata.Owners);
        Assert.Equal("desc", metadata.Metadata.Description);

        var commandText = string.Join('\n', capturedSql);
        Assert.DoesNotContain("PromptVersions", commandText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Dependency_between_versions_in_different_prompts_is_persisted_and_traversable()
    {
        Guid promptBId;
        Guid versionAId;

        using (var context = fixture.CreateContext())
        {
            var service = new PromptService(new PromptRepository(context), new HashingBagOfWordsEmbeddingProvider(), new EmbeddingStore(context));

            var promptA = await service.CreatePromptAsync("Prompt A");
            var versionA = await service.CreateVersionAsync(promptA.Id, "content A", "alice");
            versionAId = versionA.Id;

            var promptB = await service.CreatePromptAsync("Prompt B");
            promptBId = promptB.Id;
            var versionB = await service.CreateVersionAsync(promptB.Id, "content B", "alice");

            await service.AddPromptDependencyAsync(promptB.Id, versionB.Id, versionAId, PromptDependencyRelationship.Requires);
        }

        using var freshContext = fixture.CreateContext();
        var reloadedB = await new PromptRepository(freshContext).GetByIdAsync(promptBId);

        var dependency = Assert.Single(reloadedB!.Versions.Single().Dependencies);
        Assert.Equal(versionAId, dependency.TargetPromptVersionId);
        Assert.Equal(PromptDependencyRelationship.Requires, dependency.Relationship);
    }

    [Fact]
    public async Task TagPrompt_merges_new_tags_without_touching_other_metadata()
    {
        Guid promptId;
        using (var context = fixture.CreateContext())
        {
            var service = new PromptService(new PromptRepository(context), new HashingBagOfWordsEmbeddingProvider(), new EmbeddingStore(context));
            var prompt = await service.CreatePromptAsync("Fix a bug", new PromptMetadata { Tags = ["bugfix"], Description = "desc" });
            promptId = prompt.Id;

            await service.TagPromptAsync(promptId, ["backend", "bugfix"]);
        }

        using var freshContext = fixture.CreateContext();
        var metadata = await new PromptRepository(freshContext).GetMetadataAsync(promptId);

        Assert.NotNull(metadata);
        Assert.Equal(2, metadata!.Metadata.Tags.Count);
        Assert.Contains("bugfix", metadata.Metadata.Tags);
        Assert.Contains("backend", metadata.Metadata.Tags);
        Assert.Equal("desc", metadata.Metadata.Description);
    }

    [Fact]
    public async Task DeprecatePromptVersion_persists_the_status_change()
    {
        Guid promptId;
        Guid versionId;
        using (var context = fixture.CreateContext())
        {
            var service = new PromptService(new PromptRepository(context), new HashingBagOfWordsEmbeddingProvider(), new EmbeddingStore(context));
            var prompt = await service.CreatePromptAsync("Fix a bug");
            promptId = prompt.Id;
            var version = await service.CreateVersionAsync(promptId, "content", "alice");
            versionId = version.Id;

            await service.DeprecatePromptVersionAsync(promptId, versionId);
        }

        using var freshContext = fixture.CreateContext();
        var reloaded = await new PromptRepository(freshContext).GetByIdAsync(promptId);

        Assert.Equal(PromptVersionStatus.Deprecated, reloaded!.Versions.Single().Status);
    }

    [Fact]
    public async Task CreateVersion_throws_when_prompt_does_not_exist()
    {
        using var context = fixture.CreateContext();
        var service = new PromptService(new PromptRepository(context), new HashingBagOfWordsEmbeddingProvider(), new EmbeddingStore(context));

        await Assert.ThrowsAsync<PromptNotFoundException>(
            () => service.CreateVersionAsync(Guid.NewGuid(), "content", "alice"));
    }

    [Fact]
    public async Task DeprecatePromptVersion_throws_when_version_does_not_exist()
    {
        using var context = fixture.CreateContext();
        var service = new PromptService(new PromptRepository(context), new HashingBagOfWordsEmbeddingProvider(), new EmbeddingStore(context));
        var prompt = await service.CreatePromptAsync("Fix a bug");

        await Assert.ThrowsAsync<PromptVersionNotFoundException>(
            () => service.DeprecatePromptVersionAsync(prompt.Id, Guid.NewGuid()));
    }
}
