using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using PromptOps.Application.Providers;
using Xunit;

namespace PromptOps.Host.Tests;

/// <summary>
/// The one genuinely valuable end-to-end proof for automatic AI evaluation: with the policy turned
/// on, finishing an execution over real HTTP produces a persisted <c>AIEvaluation</c> with no
/// explicit <c>POST /executions/{id}/ai-evaluations</c> call anywhere in this test. Overrides
/// <see cref="IAIExecutionProvider"/> for just this factory — <c>ManualAIExecutionProvider</c> only
/// ever echoes caller-supplied parameters, and the automatic trigger never supplies any (there's no
/// per-call injection point for a server-fired background event), so exercising the real reference
/// provider here would only prove the trigger fires and then reliably fails, not that the whole
/// pipeline can actually produce a result. A stub that always returns valid judge JSON regardless
/// of input is what actually exercises the wiring end-to-end.
/// </summary>
public class AutoAIEvaluationEndToEndTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"promptops-auto-ai-evaluation-{Guid.NewGuid():N}.db");
    private readonly HttpClient _client;

    public AutoAIEvaluationEndToEndTests(WebApplicationFactory<Program> factory)
    {
        _client = factory
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:PromptOps", $"Data Source={_dbPath}");
                builder.ConfigureServices(services => services.AddSingleton<IAIExecutionProvider, AlwaysValidJudgeProvider>());
            })
            .CreateClient();
    }

    [Fact]
    public async Task Finishing_An_Execution_Automatically_Produces_An_AIEvaluation_With_No_Manual_Call()
    {
        await _client.PutAsJsonAsync("/ai-evaluation-policy", new { autoEvaluateOnFinish = true });

        var startResponse = await _client.PostAsJsonAsync("/executions/start", new
        {
            promptVersionId = Guid.NewGuid(),
            developerId = "alice",
            repository = "github.com/ctacke/PromptOps"
        });
        var started = await startResponse.Content.ReadFromJsonAsync<StartResponse>();

        await _client.PostAsJsonAsync($"/executions/{started!.ExecutionId}/finish", new
        {
            output = "diff", executionTimeMs = 1000L, filesChanged = Array.Empty<string>(), linesAdded = 1, linesDeleted = 0
        });

        // The evaluation runs in a detached background task — poll briefly rather than assuming
        // it's already done the instant /finish returns.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        List<AIEvaluationResponse>? evaluations = null;
        while (DateTime.UtcNow < deadline)
        {
            var response = await _client.GetAsync($"/executions/{started.ExecutionId}/ai-evaluations");
            evaluations = await response.Content.ReadFromJsonAsync<List<AIEvaluationResponse>>();
            if (evaluations is { Count: > 0 })
                break;
            await Task.Delay(100);
        }

        var evaluation = Assert.Single(evaluations!);
        Assert.True(evaluation.SatisfiesAcceptanceCriteria);
    }

    [Fact]
    public async Task Finishing_An_Execution_Produces_No_AIEvaluation_When_The_Policy_Is_Off()
    {
        var startResponse = await _client.PostAsJsonAsync("/executions/start", new
        {
            promptVersionId = Guid.NewGuid(),
            developerId = "alice",
            repository = "github.com/ctacke/PromptOps"
        });
        var started = await startResponse.Content.ReadFromJsonAsync<StartResponse>();

        await _client.PostAsJsonAsync($"/executions/{started!.ExecutionId}/finish", new
        {
            output = "diff", executionTimeMs = 1000L, filesChanged = Array.Empty<string>(), linesAdded = 1, linesDeleted = 0
        });
        await Task.Delay(300); // give any (unwanted) background work a chance to run

        var response = await _client.GetAsync($"/executions/{started.ExecutionId}/ai-evaluations");
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

    private sealed class AlwaysValidJudgeProvider : IAIExecutionProvider
    {
        public string Name => "always-valid-judge-stub";

        public Task<string> ExecuteAsync(string promptContent, IReadOnlyDictionary<string, string> inputs, CancellationToken cancellationToken = default)
            => Task.FromResult("""{"satisfiesAcceptanceCriteria":true,"adrViolations":[],"ignoredRequirements":[],"unnecessaryComplexityNotes":null,"suggestedPromptImprovements":[]}""");
    }

    private sealed record StartResponse(Guid ExecutionId);

    private sealed record AIEvaluationResponse(
        Guid Id, Guid ExecutionId, string JudgeProviderId, string? JudgeModel,
        bool? SatisfiesAcceptanceCriteria, List<string> AdrViolations, List<string> IgnoredRequirements,
        string? UnnecessaryComplexityNotes, List<string> SuggestedPromptImprovements,
        string RawResponse, DateTimeOffset Timestamp);
}
