using PromptOps.Domain.Evaluations;
using PromptOps.Infrastructure.Persistence;
using Xunit;

namespace PromptOps.Infrastructure.Tests;

public class HumanEvaluationIntegrationTests(SqliteFixture fixture) : IClassFixture<SqliteFixture>
{
    [Fact]
    public async Task Evaluations_round_trip_through_sqlite_and_are_ordered_by_submission_time()
    {
        var executionId = Guid.NewGuid();

        using (var dbContext = fixture.CreateContext())
        {
            var repository = new HumanEvaluationRepository(dbContext);

            var first = HumanEvaluation.Submit(
                executionId, "alice@example.com", 5, 4, 3, 5, 4, false, 5, 4,
                notes: "first pass", timestamp: DateTimeOffset.UtcNow);
            await repository.AddAsync(first);
            await repository.SaveChangesAsync();

            var second = HumanEvaluation.Submit(
                executionId, "bob@example.com", 3, 3, 4, 4, 3, true, 4, 3,
                notes: "second opinion", timestamp: DateTimeOffset.UtcNow.AddMinutes(1));
            await repository.AddAsync(second);
            await repository.SaveChangesAsync();
        }

        // Reload through a brand new context to prove this round-tripped through SQLite.
        using var freshContext = fixture.CreateContext();
        var reloaded = await new HumanEvaluationRepository(freshContext).GetByExecutionIdAsync(executionId);

        Assert.Equal(2, reloaded.Count);
        Assert.Equal("alice@example.com", reloaded[0].EvaluatorId);
        Assert.Equal(5, reloaded[0].Correctness);
        Assert.False(reloaded[0].Hallucinations);
        Assert.Equal("bob@example.com", reloaded[1].EvaluatorId);
        Assert.True(reloaded[1].Hallucinations);
        Assert.Equal("second opinion", reloaded[1].Notes);
    }

    [Fact]
    public async Task GetByExecutionIdAsync_returns_empty_for_an_execution_with_no_evaluations()
    {
        using var dbContext = fixture.CreateContext();
        var repository = new HumanEvaluationRepository(dbContext);

        var result = await repository.GetByExecutionIdAsync(Guid.NewGuid());

        Assert.Empty(result);
    }
}
