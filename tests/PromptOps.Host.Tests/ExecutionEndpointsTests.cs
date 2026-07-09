using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Xunit;

namespace PromptOps.Host.Tests;

/// <summary>End-to-end over real HTTP against the actual production DI graph — proves the ingestion API (ADR-0006) is wired correctly, not just that ExecutionService works in isolation.</summary>
public class ExecutionEndpointsTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"promptops-execution-endpoints-{Guid.NewGuid():N}.db");
    private readonly HttpClient _client;

    public ExecutionEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _client = factory
            .WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:PromptOps", $"Data Source={_dbPath}"))
            .CreateClient();
    }

    [Fact]
    public async Task Start_ToolUsage_Finish_round_trip_over_http()
    {
        var startResponse = await _client.PostAsJsonAsync("/executions/start", new
        {
            promptVersionId = Guid.NewGuid(),
            developerId = "alice",
            repository = "github.com/ctacke/PromptOps",
            branch = "main",
            commit = "abc123",
            taskId = "PROMPTOPS-1",
            inputs = new Dictionary<string, string> { ["task"] = "fix it" }
        });

        Assert.Equal(HttpStatusCode.OK, startResponse.StatusCode);
        var started = await startResponse.Content.ReadFromJsonAsync<StartResponse>();
        Assert.NotNull(started);

        var toolUsageResponse = await _client.PostAsJsonAsync(
            $"/executions/{started!.ExecutionId}/tool-usage",
            new { name = "Read", count = 2, durationMs = 50L });
        Assert.Equal(HttpStatusCode.NoContent, toolUsageResponse.StatusCode);

        var finishResponse = await _client.PostAsJsonAsync($"/executions/{started.ExecutionId}/finish", new
        {
            output = "the diff",
            executionTimeMs = 1500L,
            aiProviderId = (string?)null,
            model = (string?)null,
            modelParameters = (string?)null,
            filesChanged = new[] { "a.cs" },
            linesAdded = 5,
            linesDeleted = 1
        });
        Assert.Equal(HttpStatusCode.OK, finishResponse.StatusCode);
        var finished = await finishResponse.Content.ReadFromJsonAsync<ExecutionResponse>();
        Assert.NotNull(finished);
        Assert.Equal("Finished", finished!.Status);
        Assert.Equal("the diff", finished.Output);
        Assert.Equal(1, finished.ToolUsageCount);

        var getResponse = await _client.GetAsync($"/executions/{started.ExecutionId}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
    }

    [Fact]
    public async Task ToolUsage_returns_404_for_an_unknown_execution()
    {
        var response = await _client.PostAsJsonAsync(
            $"/executions/{Guid.NewGuid()}/tool-usage", new { name = "Read", count = 1, durationMs = 0L });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_returns_404_for_an_unknown_execution()
    {
        var response = await _client.GetAsync($"/executions/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
        GC.SuppressFinalize(this);
    }

    private sealed record StartResponse(Guid ExecutionId);

    private sealed record ExecutionResponse(
        Guid Id, Guid PromptVersionId, string DeveloperId, string Status, string? Output,
        List<string> FilesChanged, int LinesAdded, int LinesDeleted, int ToolUsageCount);
}
