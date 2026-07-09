using PromptOps.Domain.Metrics;
using PromptOps.Infrastructure.Persistence;
using Xunit;

namespace PromptOps.Infrastructure.Tests;

public class EngineeringMetricsIntegrationTests(SqliteFixture fixture) : IClassFixture<SqliteFixture>
{
    [Fact]
    public async Task Metrics_round_trip_through_sqlite_and_are_ordered_by_collection_time()
    {
        var executionId = Guid.NewGuid();

        using (var dbContext = fixture.CreateContext())
        {
            var repository = new EngineeringMetricsRepository(dbContext);

            var sonar = EngineeringMetrics.Record(
                executionId, "sonar", collectedAt: DateTimeOffset.UtcNow,
                coverage: 87.5, sonarIssues: 4, codeSmells: 2, securityFindings: 0,
                duplication: 1.2, cyclomaticComplexity: 6.5);
            await repository.AddAsync(sonar);
            await repository.SaveChangesAsync();

            var buildResult = EngineeringMetrics.Record(
                executionId, "build-result", collectedAt: DateTimeOffset.UtcNow.AddMinutes(1),
                buildSuccess: true, testSuccess: false);
            await repository.AddAsync(buildResult);
            await repository.SaveChangesAsync();
        }

        // Reload through a brand new context to prove this round-tripped through SQLite.
        using var freshContext = fixture.CreateContext();
        var reloaded = await new EngineeringMetricsRepository(freshContext).GetByExecutionIdAsync(executionId);

        Assert.Equal(2, reloaded.Count);
        Assert.Equal("sonar", reloaded[0].CollectedBy);
        Assert.Equal(87.5, reloaded[0].Coverage);
        Assert.Equal(4, reloaded[0].SonarIssues);
        Assert.Equal("build-result", reloaded[1].CollectedBy);
        Assert.True(reloaded[1].BuildSuccess);
        Assert.False(reloaded[1].TestSuccess);
    }

    [Fact]
    public async Task GetByExecutionIdAsync_returns_empty_for_an_execution_with_no_metrics()
    {
        using var dbContext = fixture.CreateContext();
        var repository = new EngineeringMetricsRepository(dbContext);

        var result = await repository.GetByExecutionIdAsync(Guid.NewGuid());

        Assert.Empty(result);
    }
}
