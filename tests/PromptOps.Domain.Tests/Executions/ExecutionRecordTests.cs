using PromptOps.Domain.Executions;
using Xunit;

namespace PromptOps.Domain.Tests.Executions;

public class ExecutionRecordTests
{
    private static DevelopmentContext Context(string repository = "PromptOps") => new() { Repository = repository };

    [Fact]
    public void Start_Rejects_Empty_DeveloperId()
    {
        Assert.Throws<ArgumentException>(() => ExecutionRecord.Start(Guid.NewGuid(), " ", Context()));
    }

    [Fact]
    public void Start_Rejects_Empty_Repository()
    {
        Assert.Throws<ArgumentException>(() => ExecutionRecord.Start(Guid.NewGuid(), "alice", Context(repository: " ")));
    }

    [Fact]
    public void Start_Defaults_Inputs_To_Empty_When_Not_Supplied()
    {
        var execution = ExecutionRecord.Start(Guid.NewGuid(), "alice", Context());

        Assert.Empty(execution.Inputs);
        Assert.Equal(ExecutionStatus.InProgress, execution.Status);
    }

    [Fact]
    public void RecordToolUsage_Appends_In_Order()
    {
        var execution = ExecutionRecord.Start(Guid.NewGuid(), "alice", Context());

        execution.RecordToolUsage("Read", 3, TimeSpan.FromMilliseconds(120));
        execution.RecordToolUsage("Edit", 1, TimeSpan.FromMilliseconds(40));

        Assert.Equal(2, execution.ToolUsage.Count);
        Assert.Equal("Read", execution.ToolUsage[0].Name);
        Assert.Equal("Edit", execution.ToolUsage[1].Name);
    }

    [Fact]
    public void RecordToolUsage_Throws_After_Finish()
    {
        var execution = ExecutionRecord.Start(Guid.NewGuid(), "alice", Context());
        execution.Finish("output", TimeSpan.FromSeconds(1), "manual", null, null, [], 0, 0);

        Assert.Throws<InvalidOperationException>(() => execution.RecordToolUsage("Read", 1, TimeSpan.Zero));
    }

    [Fact]
    public void Finish_Rejects_Negative_LinesAdded_Or_LinesDeleted()
    {
        var execution = ExecutionRecord.Start(Guid.NewGuid(), "alice", Context());

        Assert.Throws<ArgumentOutOfRangeException>(
            () => execution.Finish("output", TimeSpan.Zero, null, null, null, [], -1, 0));

        var execution2 = ExecutionRecord.Start(Guid.NewGuid(), "alice", Context());
        Assert.Throws<ArgumentOutOfRangeException>(
            () => execution2.Finish("output", TimeSpan.Zero, null, null, null, [], 0, -1));
    }

    [Fact]
    public void Finish_Throws_When_Already_Finished()
    {
        var execution = ExecutionRecord.Start(Guid.NewGuid(), "alice", Context());
        execution.Finish("output", TimeSpan.Zero, null, null, null, [], 0, 0);

        Assert.Throws<InvalidOperationException>(
            () => execution.Finish("output again", TimeSpan.Zero, null, null, null, [], 0, 0));
    }

    [Fact]
    public void Finish_Sets_Status_And_Raises_ExecutionRecorded()
    {
        var promptVersionId = Guid.NewGuid();
        var execution = ExecutionRecord.Start(promptVersionId, "alice", Context("PromptOps"));

        execution.Finish("the output", TimeSpan.FromSeconds(2), "manual", "claude", null, ["a.cs"], 10, 2);

        Assert.Equal(ExecutionStatus.Finished, execution.Status);
        Assert.Equal("the output", execution.Output);
        Assert.Equal(10, execution.LinesAdded);
        Assert.Equal(2, execution.LinesDeleted);

        var raised = Assert.Single(execution.DomainEvents);
        var recorded = Assert.IsType<ExecutionRecorded>(raised);
        Assert.Equal(execution.Id, recorded.ExecutionId);
        Assert.Equal(promptVersionId, recorded.PromptVersionId);
        Assert.Equal("PromptOps", recorded.Repository);
    }

    [Fact]
    public void ClearDomainEvents_Empties_The_List()
    {
        var execution = ExecutionRecord.Start(Guid.NewGuid(), "alice", Context());
        execution.Finish("output", TimeSpan.Zero, null, null, null, [], 0, 0);

        execution.ClearDomainEvents();

        Assert.Empty(execution.DomainEvents);
    }

    [Fact]
    public void Rehydrate_Reconstructs_Full_State_Including_ToolUsage()
    {
        var id = Guid.NewGuid();
        var promptVersionId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var toolUsage = new[] { new ToolUsage("Read", 1, TimeSpan.FromMilliseconds(10), now) };

        var execution = ExecutionRecord.Rehydrate(
            id, promptVersionId, "alice", now, Context(), new Dictionary<string, string> { ["x"] = "y" },
            ExecutionStatus.Finished, "output", TimeSpan.FromSeconds(1), "manual", "claude", null,
            ["a.cs"], 5, 1, toolUsage);

        Assert.Equal(id, execution.Id);
        Assert.Equal(ExecutionStatus.Finished, execution.Status);
        Assert.Equal("output", execution.Output);
        Assert.Single(execution.ToolUsage);
        Assert.Empty(execution.DomainEvents); // rehydration must never re-raise events
    }
}
