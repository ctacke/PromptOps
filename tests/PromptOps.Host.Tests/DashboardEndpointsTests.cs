using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Xunit;

namespace PromptOps.Host.Tests;

public class DashboardEndpointsTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"promptops-dashboard-endpoints-{Guid.NewGuid():N}.db");
    private readonly HttpClient _client;

    public DashboardEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _client = factory
            .WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:PromptOps", $"Data Source={_dbPath}"))
            .CreateClient();
    }

    [Fact]
    public async Task Dashboard_endpoints_return_successful_responses()
    {
        // 1. Check Prompts dashboard list (should be empty initially)
        var promptsResponse = await _client.GetAsync("/api/dashboard/prompts");
        Assert.Equal(HttpStatusCode.OK, promptsResponse.StatusCode);
        var prompts = await promptsResponse.Content.ReadFromJsonAsync<List<object>>();
        Assert.NotNull(prompts);

        // 2. Check Executions dashboard list (should be empty initially)
        var executionsResponse = await _client.GetAsync("/api/dashboard/executions");
        if (executionsResponse.StatusCode != HttpStatusCode.OK)
        {
            var content = await executionsResponse.Content.ReadAsStringAsync();
            throw new Exception($"Executions failed with {executionsResponse.StatusCode}: {content}");
        }
        var executions = await executionsResponse.Content.ReadFromJsonAsync<PagedResponse<object>>();
        Assert.NotNull(executions);
        Assert.Equal(0, executions.TotalCount);

        // 3. Check stats trends endpoint
        var trendsResponse = await _client.GetAsync("/api/dashboard/stats-trends");
        if (trendsResponse.StatusCode != HttpStatusCode.OK)
        {
            var content = await trendsResponse.Content.ReadAsStringAsync();
            throw new Exception($"Stats-trends failed with {trendsResponse.StatusCode}: {content}");
        }

        // 4. Non-existent execution returns 404
        var detailsResponse = await _client.GetAsync($"/api/dashboard/executions/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, detailsResponse.StatusCode);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
        GC.SuppressFinalize(this);
    }

    private sealed record PagedResponse<T>(int TotalCount, int Page, int PageSize, List<T> Items);
}
