using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Xunit;

namespace PromptOps.Host.Tests;

/// <summary>End-to-end over real HTTP against the actual production DI graph (Phase 8).</summary>
public class ScoringEndpointsTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"promptops-scoring-endpoints-{Guid.NewGuid():N}.db");
    private readonly HttpClient _client;

    public ScoringEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _client = factory
            .WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:PromptOps", $"Data Source={_dbPath}"))
            .CreateClient();
    }

    private static object HumanRatingOnlyWeights(double humanRating) => new
    {
        humanRating,
        sonar = 0.0,
        tests = 0.0,
        build = 0.0,
        acceptanceCriteria = 0.0,
        manualFixes = 0.0,
        reviewComments = 0.0,
        regressionBugs = 0.0
    };

    [Fact]
    public async Task Creating_A_Second_Config_Under_The_Same_Name_Auto_Increments_The_Version()
    {
        var name = $"test-{Guid.NewGuid():N}";

        var first = await _client.PostAsJsonAsync("/scoring-configs", new { name, weights = HumanRatingOnlyWeights(1.0) });
        var second = await _client.PostAsJsonAsync("/scoring-configs", new { name, weights = HumanRatingOnlyWeights(0.5) });

        var firstConfig = await first.Content.ReadFromJsonAsync<ScoringConfigResponse>();
        var secondConfig = await second.Content.ReadFromJsonAsync<ScoringConfigResponse>();

        Assert.Equal(1, firstConfig!.Version);
        Assert.Equal(2, secondConfig!.Version);

        var listResponse = await _client.GetAsync($"/scoring-configs?name={name}");
        var versions = await listResponse.Content.ReadFromJsonAsync<List<ScoringConfigResponse>>();
        Assert.Equal(2, versions!.Count);
    }

    [Fact]
    public async Task Recompute_With_No_Data_Yet_Returns_A_Zero_Score_Not_An_Error()
    {
        var promptVersionId = Guid.NewGuid();

        var response = await _client.PostAsJsonAsync($"/prompts/{promptVersionId}/scores", (object?)null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var score = await response.Content.ReadFromJsonAsync<PromptScoreResponse>();
        Assert.Equal(0.0, score!.OverallScore);
        Assert.Equal(0, score.SampleSize);
    }

    [Fact]
    public async Task Changing_Weights_Deterministically_Changes_The_Recomputed_Score()
    {
        var promptVersionId = Guid.NewGuid();

        var startResponse = await _client.PostAsJsonAsync("/executions/start", new
        {
            promptVersionId,
            developerId = "alice",
            repository = "github.com/ctacke/PromptOps"
        });
        var started = await startResponse.Content.ReadFromJsonAsync<StartResponse>();

        await _client.PostAsJsonAsync($"/executions/{started!.ExecutionId}/finish", new
        {
            output = "the diff",
            executionTimeMs = 1000L,
            aiProviderId = (string?)null,
            model = (string?)null,
            modelParameters = (string?)null,
            filesChanged = Array.Empty<string>(),
            linesAdded = 0,
            linesDeleted = 0
        });

        await _client.PostAsJsonAsync($"/executions/{started.ExecutionId}/evaluations", new
        {
            evaluatorId = "alice@example.com",
            correctness = 5,
            helpfulness = 5,
            architecture = 5,
            readability = 5,
            completeness = 5,
            hallucinations = false,
            confidence = 5,
            overallSatisfaction = 5, // normalizes to 100
            notes = (string?)null
        });

        var configName = $"test-{Guid.NewGuid():N}";
        var configResponse = await _client.PostAsJsonAsync("/scoring-configs", new
        {
            name = configName,
            weights = HumanRatingOnlyWeights(1.0)
        });
        var config = await configResponse.Content.ReadFromJsonAsync<ScoringConfigResponse>();

        var scoreResponse = await _client.PostAsJsonAsync($"/prompts/{promptVersionId}/scores", new { scoringConfigId = config!.Id });
        Assert.Equal(HttpStatusCode.OK, scoreResponse.StatusCode);
        var score = await scoreResponse.Content.ReadFromJsonAsync<PromptScoreResponse>();

        Assert.Equal(100.0, score!.OverallScore);
        Assert.Equal(1, score.SampleSize);
        Assert.Equal(config.Id, score.ScoringConfigId); // reproducibility: records which config version produced it

        var history = await _client.GetAsync($"/prompts/{promptVersionId}/scores");
        var scores = await history.Content.ReadFromJsonAsync<List<PromptScoreResponse>>();
        Assert.Single(scores!);
    }

    [Fact]
    public async Task Recompute_Returns_404_For_An_Unknown_ScoringConfigId()
    {
        var response = await _client.PostAsJsonAsync($"/prompts/{Guid.NewGuid()}/scores", new { scoringConfigId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetScores_returns_empty_list_for_a_prompt_version_never_scored()
    {
        var response = await _client.GetAsync($"/prompts/{Guid.NewGuid()}/scores");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var scores = await response.Content.ReadFromJsonAsync<List<PromptScoreResponse>>();
        Assert.Empty(scores!);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
        GC.SuppressFinalize(this);
    }

    private sealed record StartResponse(Guid ExecutionId);

    private sealed record ScoringWeightsResponse(
        double HumanRating, double Sonar, double Tests, double Build,
        double AcceptanceCriteria, double ManualFixes, double ReviewComments, double RegressionBugs);

    private sealed record ScoringConfigResponse(Guid Id, string Name, int Version, ScoringWeightsResponse Weights, DateTimeOffset CreatedAt);

    private sealed record PromptScoreResponse(
        Guid Id, Guid PromptVersionId, Guid ScoringConfigId, DateTimeOffset ComputedAt,
        double OverallScore, Dictionary<string, double> ComponentScores, int SampleSize);
}
