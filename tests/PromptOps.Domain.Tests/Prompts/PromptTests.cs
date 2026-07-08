using PromptOps.Domain.Prompts;
using Xunit;

namespace PromptOps.Domain.Tests.Prompts;

public class PromptTests
{
    [Fact]
    public void Create_Rejects_Empty_Name()
    {
        Assert.Throws<ArgumentException>(() => Prompt.Create(""));
    }

    [Fact]
    public void CreateVersion_Assigns_Sequential_Version_Numbers_Starting_At_One()
    {
        var prompt = Prompt.Create("Fix a bug");

        var v1 = prompt.CreateVersion("first draft", "alice");
        var v2 = prompt.CreateVersion("second draft", "alice");

        Assert.Equal(1, v1.VersionNumber);
        Assert.Equal(2, v2.VersionNumber);
        Assert.Same(v2, prompt.LatestVersion);
    }

    [Fact]
    public void CreateVersion_Links_Lineage_To_Previous_Version()
    {
        var prompt = Prompt.Create("Fix a bug");

        var v1 = prompt.CreateVersion("first draft", "alice");
        var v2 = prompt.CreateVersion("second draft", "alice");

        Assert.Null(v1.ParentVersionId);
        Assert.Equal(v1.Id, v2.ParentVersionId);
    }

    [Fact]
    public void CreateVersion_Content_Is_Immutable_After_Creation()
    {
        var prompt = Prompt.Create("Fix a bug");

        var v1 = prompt.CreateVersion("first draft", "alice");
        prompt.CreateVersion("second draft", "alice");

        // Creating a later version must never change an earlier one's content.
        Assert.Equal("first draft", v1.Content);
    }

    [Fact]
    public void CreateVersion_Rejects_Empty_Content()
    {
        var prompt = Prompt.Create("Fix a bug");

        Assert.Throws<ArgumentException>(() => prompt.CreateVersion("   ", "alice"));
    }

    [Fact]
    public void CreateVersion_Rejects_Empty_CreatedBy()
    {
        var prompt = Prompt.Create("Fix a bug");

        Assert.Throws<ArgumentException>(() => prompt.CreateVersion("content", "  "));
    }

    [Fact]
    public void Rehydrate_Rejects_Duplicate_Version_Numbers()
    {
        var promptId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var v1 = PromptVersion.Rehydrate(
            Guid.NewGuid(), promptId, versionNumber: 1, content: "a", createdBy: "alice",
            parentVersionId: null, changelogEntry: null, templateVariables: [],
            status: PromptVersionStatus.Draft, createdAt: now, dependencies: []);

        var duplicate = PromptVersion.Rehydrate(
            Guid.NewGuid(), promptId, versionNumber: 1, content: "b", createdBy: "alice",
            parentVersionId: v1.Id, changelogEntry: null, templateVariables: [],
            status: PromptVersionStatus.Draft, createdAt: now, dependencies: []);

        Assert.Throws<InvalidOperationException>(
            () => Prompt.Rehydrate(promptId, "Fix a bug", PromptMetadata.Empty, now, [v1, duplicate]));
    }

    [Fact]
    public void Rehydrate_Rejects_Version_Belonging_To_A_Different_Prompt()
    {
        var promptId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var versionForAnotherPrompt = PromptVersion.Rehydrate(
            Guid.NewGuid(), Guid.NewGuid(), versionNumber: 1, content: "a", createdBy: "alice",
            parentVersionId: null, changelogEntry: null, templateVariables: [],
            status: PromptVersionStatus.Draft, createdAt: now, dependencies: []);

        Assert.Throws<InvalidOperationException>(
            () => Prompt.Rehydrate(promptId, "Fix a bug", PromptMetadata.Empty, now, [versionForAnotherPrompt]));
    }

    [Fact]
    public void Rehydrate_Reconstructs_A_Prompt_With_Its_Versions()
    {
        var promptId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var v1 = PromptVersion.Rehydrate(
            Guid.NewGuid(), promptId, versionNumber: 1, content: "a", createdBy: "alice",
            parentVersionId: null, changelogEntry: null, templateVariables: [],
            status: PromptVersionStatus.Active, createdAt: now, dependencies: []);

        var prompt = Prompt.Rehydrate(promptId, "Fix a bug", PromptMetadata.Empty, now, [v1]);

        Assert.Equal(promptId, prompt.Id);
        Assert.Single(prompt.Versions);
        Assert.Same(v1, prompt.LatestVersion);
    }

    [Fact]
    public void UpdateMetadata_Replaces_Metadata_Without_Touching_Versions()
    {
        var prompt = Prompt.Create("Fix a bug");
        prompt.CreateVersion("first draft", "alice");

        var metadata = new PromptMetadata { Tags = ["bugfix", "backend"] };
        prompt.UpdateMetadata(metadata);

        Assert.Equal(metadata, prompt.Metadata);
        Assert.Single(prompt.Versions);
    }
}
