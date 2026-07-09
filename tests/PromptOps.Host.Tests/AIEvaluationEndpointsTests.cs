using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Xunit;

namespace PromptOps.Host.Tests;

/// <summary>
/// End-to-end over real HTTP against the actual production DI graph — the real
/// <c>AIJudgeEvaluationProvider</c> driving the real (manual/echo) <c>IAIExecutionProvider</c>,
/// fed canned judge JSON via <c>parameters.output</c> the same way <c>ManualAIExecutionProvider</c>
/// is designed to be driven (docs/execution-tracking.md).
/// </summary>
public class AIEvaluationEndpointsTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"promptops-ai-evaluation-endpoints-{Guid.NewGuid():N}.db");
    private readonly HttpClient _client;

    public AIEvaluationEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _client = factory
            .WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:PromptOps", $"Data Source={_dbPath}"))
            .CreateClient();
    }

    private async Task<Guid> StartExecutionAsync(string[]? acceptanceCriteria = null)
    {
        var startResponse = await _client.PostAsJsonAsync("/executions/start", new
        {
            promptVersionId = Guid.NewGuid(),
            developerId = "alice",
            repository = "github.com/ctacke/PromptOps",
            acceptanceCriteria = acceptanceCriteria ?? []
        });
        var started = await startResponse.Content.ReadFromJsonAsync<StartResponse>();
        return started!.ExecutionId;
    }

    [Fact]
    public async Task Run_then_retrieve_an_ai_evaluation_over_http()
    {
        var executionId = await StartExecutionAsync(["Endpoint returns 404 for unknown ids"]);

        var runResponse = await _client.PostAsJsonAsync($"/executions/{executionId}/ai-evaluations", new
        {
            parameters = new Dictionary<string, string>
            {
                ["output"] = """{"satisfiesAcceptanceCriteria":true,"adrViolations":[],"ignoredRequirements":[],"unnecessaryComplexityNotes":null,"suggestedPromptImprovements":["be more specific"]}"""
            }
        });

        Assert.Equal(HttpStatusCode.OK, runResponse.StatusCode);
        var run = await runResponse.Content.ReadFromJsonAsync<AIEvaluationResponse>();
        Assert.NotNull(run);
        Assert.True(run!.SatisfiesAcceptanceCriteria);
        Assert.Equal(["be more specific"], run.SuggestedPromptImprovements);

        var getResponse = await _client.GetAsync($"/executions/{executionId}/ai-evaluations");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var evaluations = await getResponse.Content.ReadFromJsonAsync<List<AIEvaluationResponse>>();
        var evaluation = Assert.Single(evaluations!);
        Assert.Equal(run.Id, evaluation.Id);
    }

    [Fact]
    public async Task Run_returns_502_when_the_judge_never_returns_valid_json()
    {
        var executionId = await StartExecutionAsync();

        var runResponse = await _client.PostAsJsonAsync($"/executions/{executionId}/ai-evaluations", new
        {
            parameters = new Dictionary<string, string> { ["output"] = "not json, no matter how many times you ask" }
        });

        Assert.Equal(HttpStatusCode.BadGateway, runResponse.StatusCode);
    }

    [Fact]
    public async Task Run_returns_404_for_an_unknown_execution()
    {
        var response = await _client.PostAsJsonAsync($"/executions/{Guid.NewGuid()}/ai-evaluations", new
        {
            parameters = new Dictionary<string, string> { ["output"] = """{"satisfiesAcceptanceCriteria":true}""" }
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_returns_empty_list_for_an_execution_with_no_ai_evaluations()
    {
        var executionId = await StartExecutionAsync();

        var response = await _client.GetAsync($"/executions/{executionId}/ai-evaluations");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var evaluations = await response.Content.ReadFromJsonAsync<List<AIEvaluationResponse>>();
        Assert.Empty(evaluations!);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
        GC.SuppressFinalize(this);
    }

    private sealed record StartResponse(Guid ExecutionId);

    private sealed record AIEvaluationResponse(
        Guid Id, Guid ExecutionId, string JudgeProviderId, string? JudgeModel,
        bool? SatisfiesAcceptanceCriteria, List<string> AdrViolations, List<string> IgnoredRequirements,
        string? UnnecessaryComplexityNotes, List<string> SuggestedPromptImprovements,
        string RawResponse, DateTimeOffset Timestamp);
}
