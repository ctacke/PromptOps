using Microsoft.Extensions.DependencyInjection;
using PromptOps.Application.Events;
using PromptOps.Application.Executions;
using PromptOps.Domain.Executions;
using PromptOps.Infrastructure.Persistence;
using PromptOps.Infrastructure.Providers;
using Xunit;

namespace PromptOps.Infrastructure.Tests;

public class ExecutionTrackingIntegrationTests(SqliteFixture fixture) : IClassFixture<SqliteFixture>
{
    [Fact]
    public async Task Execution_can_be_recorded_end_to_end_from_a_fixture_hook_payload()
    {
        // Shaped like what a real git-aware Claude Code hook would send: repository/branch/commit
        // known only to the hook, diff stats computed locally and pushed at Finish (ADR-0005 §9).
        var context = new DevelopmentContext
        {
            Repository = "github.com/ctacke/PromptOps",
            Branch = "feature/execution-tracking",
            Commit = "abc1234",
            TaskId = "PROMPTOPS-42",
            AcceptanceCriteria = ["Executions are recorded end to end"],
            Languages = ["csharp"]
        };

        Guid executionId;
        using (var dbContext = fixture.CreateContext())
        {
            var service = BuildExecutionService(dbContext, out _);

            var started = await service.StartExecutionAsync(
                Guid.NewGuid(), "alice", context, new Dictionary<string, string> { ["task"] = "fix the bug" });
            executionId = started.Id;

            await service.RecordToolUsageAsync(executionId, "Read", 3, TimeSpan.FromMilliseconds(120));
            await service.RecordToolUsageAsync(executionId, "Edit", 1, TimeSpan.FromMilliseconds(40));

            await service.FinishExecutionAsync(
                executionId, "the diff", TimeSpan.FromSeconds(12), aiProviderId: null, model: null, modelParameters: null,
                filesChanged: ["src/Foo.cs", "src/Bar.cs"], linesAdded: 15, linesDeleted: 3);
        }

        // Reload through a brand new context to prove this round-tripped through SQLite.
        using var freshContext = fixture.CreateContext();
        var reloaded = await new ExecutionRepository(freshContext).GetByIdAsync(executionId);

        Assert.NotNull(reloaded);
        Assert.Equal(ExecutionStatus.Finished, reloaded!.Status);
        Assert.Equal("the diff", reloaded.Output);
        Assert.Equal(2, reloaded.ToolUsage.Count);
        Assert.Equal("Read", reloaded.ToolUsage[0].Name);
        Assert.Equal("Edit", reloaded.ToolUsage[1].Name);
        Assert.Equal(2, reloaded.FilesChanged.Count);
        Assert.Equal(15, reloaded.LinesAdded);
        Assert.Equal(3, reloaded.LinesDeleted);
        Assert.Equal("github.com/ctacke/PromptOps", reloaded.Context.Repository);
        Assert.Equal(["csharp"], reloaded.Context.Languages);
        Assert.Equal("PROMPTOPS-42", reloaded.Context.TaskId);
        Assert.Equal("fix the bug", reloaded.Inputs["task"]);
    }

    [Fact]
    public async Task ExecutionRecorded_domain_event_fires_only_on_finish_and_is_observed()
    {
        using var dbContext = fixture.CreateContext();
        var service = BuildExecutionService(dbContext, out var capturedEvents);

        var context = new DevelopmentContext { Repository = "github.com/ctacke/PromptOps" };
        var started = await service.StartExecutionAsync(Guid.NewGuid(), "alice", context);

        Assert.Empty(capturedEvents);

        await service.FinishExecutionAsync(started.Id, "output", TimeSpan.FromSeconds(1), null, null, null, [], 0, 0);

        var recorded = Assert.Single(capturedEvents);
        Assert.Equal(started.Id, recorded.ExecutionId);
        Assert.Equal("github.com/ctacke/PromptOps", recorded.Repository);
    }

    [Fact]
    public async Task RecordToolUsage_throws_when_execution_does_not_exist()
    {
        using var dbContext = fixture.CreateContext();
        var service = BuildExecutionService(dbContext, out _);

        await Assert.ThrowsAsync<ExecutionNotFoundException>(
            () => service.RecordToolUsageAsync(Guid.NewGuid(), "Read", 1, TimeSpan.Zero));
    }

    [Fact]
    public async Task ExecuteAndRecordAsync_uses_the_AI_provider_and_records_its_output()
    {
        using var dbContext = fixture.CreateContext();
        var service = BuildExecutionService(dbContext, out _);
        var context = new DevelopmentContext { Repository = "github.com/ctacke/PromptOps" };

        var execution = await service.ExecuteAndRecordAsync(
            Guid.NewGuid(), "alice", context, "prompt text",
            new Dictionary<string, string> { ["output"] = "manual output" });

        Assert.Equal(ExecutionStatus.Finished, execution.Status);
        Assert.Equal("manual output", execution.Output);
        Assert.Equal("manual", execution.AiProviderId);
    }

    private static ExecutionService BuildExecutionService(PromptOpsDbContext dbContext, out List<ExecutionRecorded> capturedEvents)
    {
        var captured = new List<ExecutionRecorded>();
        capturedEvents = captured;

        var services = new ServiceCollection();
        services.AddSingleton(captured);
        services.AddSingleton<IDomainEventHandler<ExecutionRecorded>, RecordingExecutionRecordedHandler>();
        var provider = services.BuildServiceProvider();

        var repository = new ExecutionRepository(dbContext);
        var publisher = new DomainEventPublisher(provider);
        var aiExecutionProvider = new ManualAIExecutionProvider();

        return new ExecutionService(repository, publisher, aiExecutionProvider);
    }

    private sealed class RecordingExecutionRecordedHandler(List<ExecutionRecorded> captured) : IDomainEventHandler<ExecutionRecorded>
    {
        public Task HandleAsync(ExecutionRecorded domainEvent, CancellationToken cancellationToken = default)
        {
            captured.Add(domainEvent);
            return Task.CompletedTask;
        }
    }
}
