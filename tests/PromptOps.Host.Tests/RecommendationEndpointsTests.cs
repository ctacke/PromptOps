using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using PromptOps.Application.Prompts;
using PromptOps.Domain.Prompts;
using Xunit;

namespace PromptOps.Host.Tests;

/// <summary>
/// End-to-end over real HTTP against the actual production DI graph (Phase 9). There's no
/// ingestion endpoint for creating prompts (Phase 2 never got one — out of this phase's scope to
/// add), so prompt/tag/version setup goes through <see cref="PromptService"/> directly via the
/// factory's service provider; the actual thing under test, <c>POST /recommendations</c>, still
/// goes over real HTTP.
/// </summary>
public class RecommendationEndpointsTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"promptops-recommendation-endpoints-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public RecommendationEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:PromptOps", $"Data Source={_dbPath}"));
        _client = _factory.CreateClient();
    }

    private async Task<(Guid PromptId, Guid VersionId)> SeedTaggedPromptAsync(string name, string[] tags)
    {
        using var scope = _factory.Services.CreateScope();
        var promptService = scope.ServiceProvider.GetRequiredService<PromptService>();

        var prompt = await promptService.CreatePromptAsync(name);
        await promptService.TagPromptAsync(prompt.Id, tags);
        var version = await promptService.CreateVersionAsync(prompt.Id, "content", "alice");

        return (prompt.Id, version.Id);
    }

    [Fact]
    public async Task Recommend_Returns_A_Ranked_Result_With_Rationale_For_A_Matching_Prompt()
    {
        var (_, versionId) = await SeedTaggedPromptAsync("Debug Helper", ["debugging", "csharp"]);

        var startResponse = await _client.PostAsJsonAsync("/executions/start", new
        {
            promptVersionId = versionId,
            developerId = "alice",
            repository = "github.com/ctacke/PromptOps"
        });
        var started = await startResponse.Content.ReadFromJsonAsync<StartResponse>();
        await _client.PostAsJsonAsync($"/executions/{started!.ExecutionId}/finish", new
        {
            output = "diff", executionTimeMs = 1000L, filesChanged = Array.Empty<string>(), linesAdded = 0, linesDeleted = 0
        });

        var recommendResponse = await _client.PostAsJsonAsync("/recommendations", new
        {
            taskDescription = "getting a NullReferenceException, need to debug it",
            parameters = new Dictionary<string, string> { ["output"] = """["debugging"]""" }
        });

        var results = await recommendResponse.Content.ReadFromJsonAsync<List<RecommendationResponse>>();
        var result = Assert.Single(results!);
        Assert.Equal(versionId, result.RecommendedPromptVersionId);
        Assert.Contains("Matched 1/1 requested tag(s)", result.Rationale);
        Assert.Equal(1, result.Rank);
    }

    [Fact]
    public async Task Returns_An_Empty_List_When_Nothing_Matches_The_Classified_Tags()
    {
        await SeedTaggedPromptAsync("Test Writer", ["testing"]);

        var response = await _client.PostAsJsonAsync("/recommendations", new
        {
            taskDescription = "investigate a memory leak",
            parameters = new Dictionary<string, string> { ["output"] = """["debugging"]""" }
        });

        var results = await response.Content.ReadFromJsonAsync<List<RecommendationResponse>>();
        Assert.Empty(results!);
    }

    [Fact]
    public async Task Classifier_Failure_Falls_Back_To_Unfiltered_Recommendations()
    {
        var (_, versionId) = await SeedTaggedPromptAsync("Anything", ["some-tag"]);

        // A classifier response that will never parse — AIActivityClassifier degrades to an empty
        // tag list rather than failing the whole request, and an empty tag list means "no filter".
        var response = await _client.PostAsJsonAsync("/recommendations", new
        {
            taskDescription = "some task",
            parameters = new Dictionary<string, string> { ["output"] = "not json, ever" }
        });

        var results = await response.Content.ReadFromJsonAsync<List<RecommendationResponse>>();
        var result = Assert.Single(results!);
        Assert.Equal(versionId, result.RecommendedPromptVersionId);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
        GC.SuppressFinalize(this);
    }

    private sealed record StartResponse(Guid ExecutionId);

    private sealed record RecommendationResponse(string QueryContext, Guid RecommendedPromptVersionId, string Rationale, double SimilarityScore, int Rank);
}
