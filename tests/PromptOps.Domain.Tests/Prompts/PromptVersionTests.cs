using PromptOps.Domain.Prompts;
using Xunit;

namespace PromptOps.Domain.Tests.Prompts;

public class PromptVersionTests
{
    [Fact]
    public void New_Version_Starts_In_Draft_Status()
    {
        var prompt = Prompt.Create("Fix a bug");
        var version = prompt.CreateVersion("content", "alice");

        Assert.Equal(PromptVersionStatus.Draft, version.Status);
    }

    [Fact]
    public void Activate_Moves_Draft_To_Active()
    {
        var prompt = Prompt.Create("Fix a bug");
        var version = prompt.CreateVersion("content", "alice");

        version.Activate();

        Assert.Equal(PromptVersionStatus.Active, version.Status);
    }

    [Fact]
    public void Activate_Throws_When_Not_Draft()
    {
        var prompt = Prompt.Create("Fix a bug");
        var version = prompt.CreateVersion("content", "alice");
        version.Activate();

        Assert.Throws<InvalidOperationException>(() => version.Activate());
    }

    [Fact]
    public void Deprecate_Throws_When_Already_Deprecated()
    {
        var prompt = Prompt.Create("Fix a bug");
        var version = prompt.CreateVersion("content", "alice");
        version.Deprecate();

        Assert.Throws<InvalidOperationException>(() => version.Deprecate());
    }

    [Fact]
    public void Deprecate_Allowed_Directly_From_Draft()
    {
        var prompt = Prompt.Create("Fix a bug");
        var version = prompt.CreateVersion("content", "alice");

        version.Deprecate();

        Assert.Equal(PromptVersionStatus.Deprecated, version.Status);
    }

    [Fact]
    public void AddDependency_Rejects_Self_Dependency()
    {
        var prompt = Prompt.Create("Fix a bug");
        var version = prompt.CreateVersion("content", "alice");

        Assert.Throws<InvalidOperationException>(
            () => version.AddDependency(version.Id, PromptDependencyRelationship.References));
    }

    [Fact]
    public void AddDependency_Is_Idempotent_For_The_Same_Target_And_Relationship()
    {
        var prompt = Prompt.Create("Fix a bug");
        var version = prompt.CreateVersion("content", "alice");
        var targetId = Guid.NewGuid();

        version.AddDependency(targetId, PromptDependencyRelationship.Requires);
        version.AddDependency(targetId, PromptDependencyRelationship.Requires);

        Assert.Single(version.Dependencies);
    }

    [Fact]
    public void AddDependency_Allows_Multiple_Relationships_To_The_Same_Target()
    {
        var prompt = Prompt.Create("Fix a bug");
        var version = prompt.CreateVersion("content", "alice");
        var targetId = Guid.NewGuid();

        version.AddDependency(targetId, PromptDependencyRelationship.Requires);
        version.AddDependency(targetId, PromptDependencyRelationship.ExtendsFrom);

        Assert.Equal(2, version.Dependencies.Count);
    }
}
