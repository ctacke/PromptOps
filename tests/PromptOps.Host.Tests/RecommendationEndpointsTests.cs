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
/// End-to-end over real HTTP against the actual production DI graph (Phase 9, extended in
/// Phase 10). There's no ingestion endpoint for creating prompts (Phase 2 never got one — out of
/// scope to add here), so prompt/tag/version setup goes through <see cref="PromptService"/>
/// directly via the factory's service provider — which also means <see cref="PromptService"/>'s
/// Phase 10 embedding-indexing runs for real, using the real (non-stub)
/// <c>HashingBagOfWordsEmbeddingProvider</c>. The actual thing under test, <c>POST /recommendations</c>,
/// still goes over real HTTP, and resolves to <c>SemanticRecommendationProvider</c> (v2) — the
/// bound <c>IRecommendationProvider</c> as of Phase 10, not v1.
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

    private async Task<(Guid PromptId, Guid VersionId)> SeedTaggedPromptAsync(string name, string[] tags, string content = "content")
    {
        using var scope = _factory.Services.CreateScope();
        var promptService = scope.ServiceProvider.GetRequiredService<PromptService>();

        var prompt = await promptService.CreatePromptAsync(name);
        await promptService.TagPromptAsync(prompt.Id, tags);
        var version = await promptService.CreateVersionAsync(prompt.Id, content, "alice");

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
    public async Task A_Tag_Mismatched_Candidate_Still_Surfaces_Since_V2_Blends_Rather_Than_Excludes()
    {
        // v1 (Phase 9) would have excluded this entirely on zero tag overlap. v2 (Phase 10) must
        // not — excluding zero-tag-overlap candidates outright is exactly what would make
        // "semantically similar surfaces without exact tag overlap" impossible.
        await SeedTaggedPromptAsync("Test Writer", ["testing"]);

        var response = await _client.PostAsJsonAsync("/recommendations", new
        {
            taskDescription = "investigate a memory leak",
            parameters = new Dictionary<string, string> { ["output"] = """["debugging"]""" }
        });

        var results = await response.Content.ReadFromJsonAsync<List<RecommendationResponse>>();
        var result = Assert.Single(results!);
        Assert.Contains("Matched 0/1 requested tag(s)", result.Rationale);
    }

    [Fact]
    public async Task A_Semantically_Similar_Task_Ranks_Above_An_Unrelated_One_Despite_Neither_Matching_Tags()
    {
        // The phase's core acceptance criterion, proven at the HTTP level with the real (word-
        // overlap-based) HashingBagOfWordsEmbeddingProvider — no stubbing of the embedding step.
        // Classification is stubbed (ManualAIExecutionProvider) to a tag that matches *neither*
        // candidate, so only semantic similarity (driven by shared vocabulary in the prompt's
        // content) can explain the ranking difference.
        var (_, relevantVersionId) = await SeedTaggedPromptAsync(
            "Null Reference Debugger",
            tags: ["exception-handling"],
            content: "Investigate the null reference exception and debugging steps needed to fix it.");
        await SeedTaggedPromptAsync(
            "Changelog Writer",
            tags: ["documentation"],
            content: "Summarize recent commits into a concise changelog entry for release notes.");

        var response = await _client.PostAsJsonAsync("/recommendations", new
        {
            taskDescription = "getting a null reference exception, need help debugging it",
            repository = (string?)null,
            limit = 5,
            parameters = new Dictionary<string, string> { ["output"] = """["unrelated-tag"]""" }
        });

        var results = await response.Content.ReadFromJsonAsync<List<RecommendationResponse>>();
        Assert.NotEmpty(results!);
        Assert.Equal(relevantVersionId, results![0].RecommendedPromptVersionId);
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
