using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using PromptOps.Application.Prompts;
using PromptOps.Application.Refinement;
using PromptOps.Domain.Refinement;
using Xunit;

namespace PromptOps.Host.Tests;

/// <summary>
/// End-to-end over real HTTP against the production DI graph for Phase 15's <c>POST
/// /executions/start-attributed</c>. Classification is driven deterministically via the
/// <c>ManualAIExecutionProvider</c>, which echoes back <c>parameters["output"]</c> — so each test
/// controls exactly what activity tags the classifier "returns" (a JSON array), the same technique
/// <see cref="RecommendationEndpointsTests"/> uses. That lets us exercise the untracked / recommended
/// / captured branches without a real model.
/// </summary>
public class ExecutionAttributionEndpointsTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private static readonly Guid Untracked = Guid.Empty;

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"promptops-attribution-endpoints-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public ExecutionAttributionEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:PromptOps", $"Data Source={_dbPath}"));
        _client = _factory.CreateClient();
    }

    private static object Request(string prompt, string[] classifierTags) => new
    {
        prompt,
        developerId = "alice",
        repository = "github.com/ctacke/PromptOps",
        parameters = new Dictionary<string, string> { ["output"] = System.Text.Json.JsonSerializer.Serialize(classifierTags) }
    };

    [Fact]
    public async Task Non_Development_Task_Is_Left_Untracked()
    {
        // Classifier returns no tags — the daemon's signal for "not a development activity".
        var response = await _client.PostAsJsonAsync("/executions/start-attributed", Request("how are baseball stats derived?", []));
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<AttributedResponse>();

        Assert.Equal("untracked", result!.Attribution);
        Assert.Equal(Untracked, result.PromptVersionId);
        Assert.Null(result.Content);
    }

    [Fact]
    public async Task Development_Task_With_No_Matching_Prompt_Captures_A_New_One()
    {
        var response = await _client.PostAsJsonAsync("/executions/start-attributed",
            Request("help me debug this NullReferenceException", ["debugging", "csharp"]));
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<AttributedResponse>();

        Assert.Equal("captured", result!.Attribution);
        Assert.NotEqual(Untracked, result.PromptVersionId);

        // A new, findable prompt now exists and the execution really persisted against it.
        var execution = await _client.GetFromJsonAsync<ExecutionResponse>($"/executions/{result.ExecutionId}");
        Assert.Equal(result.PromptVersionId, execution!.PromptVersionId);

        using var scope = _factory.Services.CreateScope();
        var promptService = scope.ServiceProvider.GetRequiredService<PromptService>();
        var names = await promptService.ListAsync();
        Assert.Contains(names, p => p.Name == "Debugging");
    }

    [Fact]
    public async Task Development_Task_With_A_Matching_Prompt_Attributes_To_It_And_Returns_Its_Content()
    {
        var versionId = await SeedActivePromptAsync("Debug Helper", ["debugging"], content: "Reproduce the failure, then bisect.");

        var response = await _client.PostAsJsonAsync("/executions/start-attributed",
            Request("getting a NullReferenceException, need to debug it", ["debugging"]));
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<AttributedResponse>();

        Assert.Equal("recommended", result!.Attribution);
        Assert.Equal(versionId, result.PromptVersionId);
        Assert.Equal("Reproduce the failure, then bisect.", result.Content);
    }

    [Fact]
    public async Task An_Unrelated_Activity_Does_Not_Match_A_Prompt_For_A_Different_Activity()
    {
        // Only a "create a feature" prompt exists; a debugging task must capture its own prompt,
        // not attribute to the feature prompt — the exact product intent for Phase 15.
        await SeedActivePromptAsync("Feature Builder", ["code-authoring"], content: "Scaffold the feature.");

        var response = await _client.PostAsJsonAsync("/executions/start-attributed",
            Request("debug this failing test", ["debugging"]));
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<AttributedResponse>();

        Assert.Equal("captured", result!.Attribution);
    }

    [Fact]
    public async Task A_Captured_Prompt_Is_Reused_By_The_Next_Same_Activity_Task_Instead_Of_Recaptured()
    {
        var first = await _client.PostAsJsonAsync("/executions/start-attributed",
            Request("debug the login crash", ["debugging"]));
        var firstResult = await first.Content.ReadFromJsonAsync<AttributedResponse>();
        Assert.Equal("captured", firstResult!.Attribution);

        var second = await _client.PostAsJsonAsync("/executions/start-attributed",
            Request("debug the signup crash", ["debugging"]));
        var secondResult = await second.Content.ReadFromJsonAsync<AttributedResponse>();

        Assert.Equal("recommended", secondResult!.Attribution);
        Assert.Equal(firstResult.PromptVersionId, secondResult.PromptVersionId);
    }

    [Fact]
    public async Task With_Ab_Exploration_A_Session_Is_Routed_To_The_Eligible_Draft()
    {
        // An active "Debugging" prompt plus a benchmark-passing (AbEligible) refined draft, and a
        // policy that routes 100% of traffic to it — the deterministic end of the ε-greedy range.
        var (promptId, activeId) = await SeedActivePromptWithIdsAsync("Debug Helper", ["debugging"], "active content");
        var draftId = await SeedEligibleDraftAsync(promptId, activeId, "REFINED debugging content");
        await SetExplorationRateAsync(1.0);

        var response = await _client.PostAsJsonAsync("/executions/start-attributed",
            Request("debug this crash", ["debugging"]));
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<AttributedResponse>();

        Assert.Equal("recommended", result!.Attribution);
        Assert.Equal(draftId, result.PromptVersionId); // routed to the draft, not the active version
        Assert.Equal("REFINED debugging content", result.Content);
    }

    [Fact]
    public async Task Without_Ab_Exploration_A_Session_Uses_The_Active_Version()
    {
        var (promptId, activeId) = await SeedActivePromptWithIdsAsync("Debug Helper", ["debugging"], "active content");
        await SeedEligibleDraftAsync(promptId, activeId, "REFINED debugging content");
        // Default policy: AbExplorationRate = 0 → never routes to the draft.

        var response = await _client.PostAsJsonAsync("/executions/start-attributed",
            Request("debug this crash", ["debugging"]));
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<AttributedResponse>();

        Assert.Equal(activeId, result!.PromptVersionId);
        Assert.Equal("active content", result.Content);
    }

    private async Task<Guid> SeedActivePromptAsync(string name, string[] tags, string content)
    {
        var (_, versionId) = await SeedActivePromptWithIdsAsync(name, tags, content);
        return versionId;
    }

    private async Task<(Guid PromptId, Guid ActiveVersionId)> SeedActivePromptWithIdsAsync(string name, string[] tags, string content)
    {
        using var scope = _factory.Services.CreateScope();
        var promptService = scope.ServiceProvider.GetRequiredService<PromptService>();

        var prompt = await promptService.CreatePromptAsync(name);
        await promptService.TagPromptAsync(prompt.Id, tags);
        var version = await promptService.CreateVersionAsync(prompt.Id, content, "alice");
        await promptService.ActivateVersionAsync(prompt.Id, version.Id);
        return (prompt.Id, version.Id);
    }

    private async Task<Guid> SeedEligibleDraftAsync(Guid promptId, Guid activeVersionId, string draftContent)
    {
        using var scope = _factory.Services.CreateScope();
        var promptService = scope.ServiceProvider.GetRequiredService<PromptService>();
        var candidateRepository = scope.ServiceProvider.GetRequiredService<IRefinementCandidateRepository>();

        var draft = await promptService.CreateVersionAsync(promptId, draftContent, "promptops-refinement");
        var candidate = RefinementCandidate.Create(promptId, draft.Id, activeVersionId);
        candidate.MarkEligible(activeScore: 70, candidateScore: 85);
        await candidateRepository.AddAsync(candidate);
        await candidateRepository.SaveChangesAsync();
        return draft.Id;
    }

    private async Task SetExplorationRateAsync(double rate)
    {
        using var scope = _factory.Services.CreateScope();
        var policyService = scope.ServiceProvider.GetRequiredService<RefinementPolicyService>();
        await policyService.UpdateAsync(autoRefinementEnabled: false, syntheticSampleSize: 0, minQualityDelta: 0, abExplorationRate: rate);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
        GC.SuppressFinalize(this);
    }

    private sealed record AttributedResponse(Guid ExecutionId, Guid PromptVersionId, string Attribution, string? Content, string? Rationale);

    private sealed record ExecutionResponse(Guid Id, Guid PromptVersionId, string DeveloperId, string Status);
}
