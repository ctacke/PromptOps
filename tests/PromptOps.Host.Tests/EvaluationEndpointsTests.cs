using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Xunit;

namespace PromptOps.Host.Tests;

/// <summary>End-to-end over real HTTP against the actual production DI graph — proves the evaluation ingestion API (Phase 6) is wired correctly.</summary>
public class EvaluationEndpointsTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"promptops-evaluation-endpoints-{Guid.NewGuid():N}.db");
    private readonly HttpClient _client;

    public EvaluationEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _client = factory
            .WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:PromptOps", $"Data Source={_dbPath}"))
            .CreateClient();
    }

    [Fact]
    public async Task Submit_then_retrieve_a_human_evaluation_over_http()
    {
        var startResponse = await _client.PostAsJsonAsync("/executions/start", new
        {
            promptVersionId = Guid.NewGuid(),
            developerId = "alice",
            repository = "github.com/ctacke/PromptOps"
        });
        var started = await startResponse.Content.ReadFromJsonAsync<StartResponse>();

        var submitResponse = await _client.PostAsJsonAsync($"/executions/{started!.ExecutionId}/evaluations", new
        {
            evaluatorId = "alice@example.com",
            correctness = 5,
            helpfulness = 4,
            architecture = 3,
            readability = 5,
            completeness = 4,
            hallucinations = false,
            confidence = 5,
            overallSatisfaction = 4,
            notes = "solid"
        });

        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);
        var submitted = await submitResponse.Content.ReadFromJsonAsync<EvaluationResponse>();
        Assert.NotNull(submitted);
        Assert.Equal("alice@example.com", submitted!.EvaluatorId);
        Assert.Equal(5, submitted.Correctness);

        var getResponse = await _client.GetAsync($"/executions/{started.ExecutionId}/evaluations");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var evaluations = await getResponse.Content.ReadFromJsonAsync<List<EvaluationResponse>>();
        Assert.NotNull(evaluations);
        var evaluation = Assert.Single(evaluations!);
        Assert.Equal(submitted.Id, evaluation.Id);
        Assert.Equal("solid", evaluation.Notes);
    }

    [Fact]
    public async Task Submit_returns_404_for_an_unknown_execution()
    {
        var response = await _client.PostAsJsonAsync($"/executions/{Guid.NewGuid()}/evaluations", new
        {
            evaluatorId = "alice",
            correctness = 5,
            helpfulness = 5,
            architecture = 5,
            readability = 5,
            completeness = 5,
            hallucinations = false,
            confidence = 5,
            overallSatisfaction = 5,
            notes = (string?)null
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Submit_returns_400_for_an_out_of_range_rating()
    {
        var startResponse = await _client.PostAsJsonAsync("/executions/start", new
        {
            promptVersionId = Guid.NewGuid(),
            developerId = "alice",
            repository = "github.com/ctacke/PromptOps"
        });
        var started = await startResponse.Content.ReadFromJsonAsync<StartResponse>();

        var response = await _client.PostAsJsonAsync($"/executions/{started!.ExecutionId}/evaluations", new
        {
            evaluatorId = "alice",
            correctness = 99,
            helpfulness = 5,
            architecture = 5,
            readability = 5,
            completeness = 5,
            hallucinations = false,
            confidence = 5,
            overallSatisfaction = 5,
            notes = (string?)null
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_returns_empty_list_for_an_execution_with_no_evaluations()
    {
        var startResponse = await _client.PostAsJsonAsync("/executions/start", new
        {
            promptVersionId = Guid.NewGuid(),
            developerId = "alice",
            repository = "github.com/ctacke/PromptOps"
        });
        var started = await startResponse.Content.ReadFromJsonAsync<StartResponse>();

        var response = await _client.GetAsync($"/executions/{started!.ExecutionId}/evaluations");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var evaluations = await response.Content.ReadFromJsonAsync<List<EvaluationResponse>>();
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

    private sealed record EvaluationResponse(
        Guid Id, Guid ExecutionId, string EvaluatorId, int Correctness, int Helpfulness,
        int Architecture, int Readability, int Completeness, bool Hallucinations, int Confidence,
        int OverallSatisfaction, string? Notes, DateTimeOffset Timestamp);
}
