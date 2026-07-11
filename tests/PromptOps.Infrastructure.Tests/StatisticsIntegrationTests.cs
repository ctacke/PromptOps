using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PromptOps.Domain.Evaluations;
using PromptOps.Domain.Executions;
using PromptOps.Domain.Prompts;
using PromptOps.Domain.Scoring;
using PromptOps.Infrastructure.Persistence;
using Xunit;

namespace PromptOps.Infrastructure.Tests;

/// <summary>
/// Deliberately does *not* use the shared-per-class <see cref="SqliteFixture"/> pattern most other
/// integration tests in this project use — these are global counts across the whole database, so
/// (same reasoning as <c>PromotionPolicyRepositoryTests</c>) a row inserted by one <c>[Fact]</c>
/// would otherwise leak into every other test's count. Each test gets its own throwaway SQLite file.
/// </summary>
public class StatisticsIntegrationTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"promptops-statistics-tests-{Guid.NewGuid():N}.db");

    private PromptOpsDbContext CreateContext()
    {
        var context = new PromptOpsDbContext(new DbContextOptionsBuilder<PromptOpsDbContext>().UseSqlite($"Data Source={_dbPath}").Options);
        context.Database.Migrate();
        return context;
    }

    [Fact]
    public async Task PromptRepository_GetStatisticsAsync_counts_prompts_and_versions_by_status()
    {
        using (var context = CreateContext())
        {
            var repository = new PromptRepository(context);

            var promptA = Prompt.Create("A");
            promptA.CreateVersion("v1", "alice"); // Draft
            var toActivate = promptA.CreateVersion("v2", "alice");
            promptA.ActivateVersion(toActivate.Id); // Active
            await repository.AddAsync(promptA);

            var promptB = Prompt.Create("B");
            promptB.CreateVersion("v1", "alice"); // Draft
            await repository.AddAsync(promptB);

            await repository.SaveChangesAsync();
        }

        using var freshContext = CreateContext();
        var stats = await new PromptRepository(freshContext).GetStatisticsAsync();

        Assert.Equal(2, stats.PromptCount);
        Assert.Equal(3, stats.VersionCount);
        Assert.Equal(2, stats.VersionCountByStatus[nameof(PromptVersionStatus.Draft)]);
        Assert.Equal(1, stats.VersionCountByStatus[nameof(PromptVersionStatus.Active)]);
    }

    [Fact]
    public async Task ExecutionRepository_GetStatisticsAsync_counts_by_status_and_repository()
    {
        using (var context = CreateContext())
        {
            var repository = new ExecutionRepository(context);

            var inProgress = ExecutionRecord.Start(Guid.NewGuid(), "alice", new DevelopmentContext { Repository = "repo-a" });
            await repository.AddAsync(inProgress);

            var finishedA = ExecutionRecord.Start(Guid.NewGuid(), "alice", new DevelopmentContext { Repository = "repo-a" });
            finishedA.Finish("out", TimeSpan.FromSeconds(1), "manual", null, null, [], 0, 0);
            await repository.AddAsync(finishedA);

            var finishedB = ExecutionRecord.Start(Guid.NewGuid(), "alice", new DevelopmentContext { Repository = "repo-b" });
            finishedB.Finish("out", TimeSpan.FromSeconds(1), "manual", null, null, [], 0, 0);
            await repository.AddAsync(finishedB);

            await repository.SaveChangesAsync();
        }

        using var freshContext = CreateContext();
        var stats = await new ExecutionRepository(freshContext).GetStatisticsAsync();

        Assert.Equal(3, stats.TotalCount);
        Assert.Equal(1, stats.CountByStatus[nameof(ExecutionStatus.InProgress)]);
        Assert.Equal(2, stats.CountByStatus[nameof(ExecutionStatus.Finished)]);
        Assert.Equal(2, stats.CountByRepository["repo-a"]);
        Assert.Equal(1, stats.CountByRepository["repo-b"]);
    }

    [Fact]
    public async Task PromptScoreRepository_GetStatisticsAsync_returns_null_average_when_empty()
    {
        using var context = CreateContext();

        var stats = await new PromptScoreRepository(context).GetStatisticsAsync();

        Assert.Equal(0, stats.Count);
        Assert.Null(stats.AverageOverallScore);
    }

    [Fact]
    public async Task PromptScoreRepository_GetStatisticsAsync_counts_and_averages_scores()
    {
        using (var context = CreateContext())
        {
            var repository = new PromptScoreRepository(context);
            await repository.AddAsync(PromptScore.Compute(Guid.NewGuid(), Guid.NewGuid(), 80.0, new Dictionary<string, double>(), 1));
            await repository.AddAsync(PromptScore.Compute(Guid.NewGuid(), Guid.NewGuid(), 90.0, new Dictionary<string, double>(), 1));
            await repository.SaveChangesAsync();
        }

        using var freshContext = CreateContext();
        var stats = await new PromptScoreRepository(freshContext).GetStatisticsAsync();

        Assert.Equal(2, stats.Count);
        Assert.Equal(85.0, stats.AverageOverallScore);
    }

    [Fact]
    public async Task HumanEvaluationRepository_GetCountAsync_counts_every_evaluation()
    {
        using (var context = CreateContext())
        {
            var repository = new HumanEvaluationRepository(context);
            await repository.AddAsync(HumanEvaluation.Submit(Guid.NewGuid(), "alice", 5, 5, 5, 5, 5, false, 5, 5));
            await repository.AddAsync(HumanEvaluation.Submit(Guid.NewGuid(), "bob", 4, 4, 4, 4, 4, false, 4, 4));
            await repository.SaveChangesAsync();
        }

        using var freshContext = CreateContext();
        var count = await new HumanEvaluationRepository(freshContext).GetCountAsync();

        Assert.Equal(2, count);
    }

    [Fact]
    public async Task AIEvaluationRepository_GetCountAsync_counts_every_evaluation()
    {
        using (var context = CreateContext())
        {
            var repository = new AIEvaluationRepository(context);
            await repository.AddAsync(AIEvaluation.Record(Guid.NewGuid(), "manual", null, true, [], [], null, [], "{}"));
            await repository.SaveChangesAsync();
        }

        using var freshContext = CreateContext();
        var count = await new AIEvaluationRepository(freshContext).GetCountAsync();

        Assert.Equal(1, count);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
        GC.SuppressFinalize(this);
    }
}
