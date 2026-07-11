using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using PromptOps.Application.Prompts;
using Xunit;

namespace PromptOps.Host.Tests;

/// <summary>
/// End-to-end over real HTTP against the actual production DI graph. Each test method gets its own
/// fresh SQLite file (the constructor runs per <c>[Fact]</c>, generating a new db path each time —
/// see <see cref="_dbPath"/>), so exact-count assertions are safe without the shared-fixture
/// isolation concerns <c>StatisticsIntegrationTests</c> has to work around.
/// </summary>
public class StatisticsEndpointsTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"promptops-statistics-endpoints-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public StatisticsEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:PromptOps", $"Data Source={_dbPath}"));
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task Get_Statistics_Returns_All_Zeroes_On_A_Fresh_Database()
    {
        var response = await _client.GetAsync("/statistics");

        var stats = await response.Content.ReadFromJsonAsync<SystemStatisticsResponse>();
        Assert.Equal(0, stats!.Prompts.PromptCount);
        Assert.Equal(0, stats.Executions.TotalCount);
        Assert.Equal(0, stats.Scores.Count);
        Assert.Null(stats.Scores.AverageOverallScore);
        Assert.Equal(0, stats.HumanEvaluationCount);
        Assert.Equal(0, stats.AIEvaluationCount);
    }

    [Fact]
    public async Task Get_Statistics_Reflects_Seeded_Prompts()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var promptService = scope.ServiceProvider.GetRequiredService<PromptService>();
            var promptA = await promptService.CreatePromptAsync("A");
            await promptService.CreateVersionAsync(promptA.Id, "content", "alice");
            var promptB = await promptService.CreatePromptAsync("B");
            await promptService.CreateVersionAsync(promptB.Id, "content", "alice");
        }

        var response = await _client.GetAsync("/statistics");

        var stats = await response.Content.ReadFromJsonAsync<SystemStatisticsResponse>();
        Assert.Equal(2, stats!.Prompts.PromptCount);
        Assert.Equal(2, stats.Prompts.VersionCount);
        Assert.Equal(2, stats.Prompts.VersionCountByStatus["Draft"]);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
        GC.SuppressFinalize(this);
    }

    private sealed record PromptStatisticsResponse(int PromptCount, int VersionCount, Dictionary<string, int> VersionCountByStatus);
    private sealed record ExecutionStatisticsResponse(int TotalCount, Dictionary<string, int> CountByStatus, Dictionary<string, int> CountByRepository);
    private sealed record ScoreStatisticsResponse(int Count, double? AverageOverallScore);
    private sealed record SystemStatisticsResponse(
        PromptStatisticsResponse Prompts, ExecutionStatisticsResponse Executions, ScoreStatisticsResponse Scores,
        int HumanEvaluationCount, int AIEvaluationCount);
}
