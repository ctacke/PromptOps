using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using PromptOps.Application.Prompts;
using PromptOps.Domain.Prompts;
using Xunit;

namespace PromptOps.Host.Tests;

/// <summary>
/// End-to-end over real HTTP for the <c>/prompts</c> REST surface: creation (closing the
/// ingestion gap every smoke test from Phase 9 through Phase 11 flagged as out of scope) and the
/// manual activation primitive (Phase 11). <see cref="SeedVersionAsync"/> still seeds via
/// <see cref="PromptService"/> directly for tests that aren't themselves testing creation, same
/// pattern <c>RecommendationEndpointsTests</c> uses.
/// </summary>
public class PromptEndpointsTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"promptops-prompt-endpoints-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public PromptEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:PromptOps", $"Data Source={_dbPath}"));
        _client = _factory.CreateClient();
    }

    private async Task<(Guid PromptId, Guid VersionId)> SeedVersionAsync(string content = "content")
    {
        using var scope = _factory.Services.CreateScope();
        var promptService = scope.ServiceProvider.GetRequiredService<PromptService>();

        var prompt = await promptService.CreatePromptAsync("Fix a bug");
        var version = await promptService.CreateVersionAsync(prompt.Id, content, "alice");

        return (prompt.Id, version.Id);
    }

    [Fact]
    public async Task Create_Prompt_Returns_Ok_With_The_New_Prompts_Id()
    {
        var response = await _client.PostAsJsonAsync("/prompts", new { name = "Fix a bug", metadata = (object?)null });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<PromptResponse>();
        Assert.NotEqual(Guid.Empty, created!.Id);
        Assert.Equal("Fix a bug", created.Name);
    }

    [Fact]
    public async Task Create_Prompt_Returns_BadRequest_For_An_Empty_Name()
    {
        var response = await _client.PostAsJsonAsync("/prompts", new { name = "", metadata = (object?)null });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_Prompt_Returns_The_Metadata_For_A_Just_Created_Prompt()
    {
        var createResponse = await _client.PostAsJsonAsync("/prompts", new
        {
            name = "Fix a bug",
            metadata = new { description = "desc", tags = new[] { "bugfix" } }
        });
        var created = await createResponse.Content.ReadFromJsonAsync<PromptResponse>();

        var response = await _client.GetAsync($"/prompts/{created!.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var metadata = await response.Content.ReadFromJsonAsync<PromptMetadataResponse>();
        Assert.Equal("Fix a bug", metadata!.Name);
        Assert.Equal("desc", metadata.Metadata.Description);
        Assert.Contains("bugfix", metadata.Metadata.Tags!);
    }

    [Fact]
    public async Task Get_Prompt_Returns_NotFound_For_An_Unknown_Prompt()
    {
        var response = await _client.GetAsync($"/prompts/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task List_Prompts_Returns_Empty_Array_On_A_Fresh_Database()
    {
        var response = await _client.GetAsync("/prompts");

        var prompts = await response.Content.ReadFromJsonAsync<List<PromptSummaryResponse>>();
        Assert.Empty(prompts!);
    }

    [Fact]
    public async Task List_Prompts_Returns_Every_Created_Prompts_Id_And_Name()
    {
        var first = await (await _client.PostAsJsonAsync("/prompts", new { name = "Fix a Bug", metadata = (object?)null })).Content.ReadFromJsonAsync<PromptResponse>();
        var second = await (await _client.PostAsJsonAsync("/prompts", new { name = "Write Tests", metadata = (object?)null })).Content.ReadFromJsonAsync<PromptResponse>();

        var response = await _client.GetAsync("/prompts");

        var prompts = await response.Content.ReadFromJsonAsync<List<PromptSummaryResponse>>();
        Assert.Equal(2, prompts!.Count);
        Assert.Contains(prompts, p => p.Id == first!.Id && p.Name == "Fix a Bug");
        Assert.Contains(prompts, p => p.Id == second!.Id && p.Name == "Write Tests");
    }

    [Fact]
    public async Task Create_Version_Returns_Ok_And_Starts_As_Draft()
    {
        var createResponse = await _client.PostAsJsonAsync("/prompts", new { name = "Fix a bug", metadata = (object?)null });
        var prompt = await createResponse.Content.ReadFromJsonAsync<PromptResponse>();

        var response = await _client.PostAsJsonAsync($"/prompts/{prompt!.Id}/versions", new { content = "content", createdBy = "alice" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var version = await response.Content.ReadFromJsonAsync<PromptVersionResponse>();
        Assert.Equal(1, version!.VersionNumber);
        Assert.Equal(PromptVersionStatus.Draft, version.Status);
    }

    [Fact]
    public async Task Create_Version_Returns_NotFound_For_An_Unknown_Prompt()
    {
        var response = await _client.PostAsJsonAsync($"/prompts/{Guid.NewGuid()}/versions", new { content = "content", createdBy = "alice" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_Prompt_Version_Returns_Its_Content()
    {
        var createResponse = await _client.PostAsJsonAsync("/prompts", new { name = "Fix a Bug", metadata = new { tags = new[] { "debugging" } } });
        var prompt = await createResponse.Content.ReadFromJsonAsync<PromptResponse>();
        var versionResponse = await _client.PostAsJsonAsync($"/prompts/{prompt!.Id}/versions", new { content = "Investigate and fix the bug.", createdBy = "alice" });
        var version = await versionResponse.Content.ReadFromJsonAsync<PromptVersionResponse>();

        var response = await _client.GetAsync($"/prompt-versions/{version!.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var detail = await response.Content.ReadFromJsonAsync<PromptVersionDetailResponse>();
        Assert.Equal(prompt.Id, detail!.PromptId);
        Assert.Equal("Fix a Bug", detail.PromptName);
        Assert.Equal("Investigate and fix the bug.", detail.Content);
        Assert.Equal(PromptVersionStatus.Draft, detail.Status);
        Assert.Contains("debugging", detail.Tags);
    }

    [Fact]
    public async Task Get_Prompt_Version_Returns_NotFound_For_An_Unknown_Version()
    {
        var response = await _client.GetAsync($"/prompt-versions/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Create_Version_Returns_BadRequest_For_Empty_Content()
    {
        var createResponse = await _client.PostAsJsonAsync("/prompts", new { name = "Fix a bug", metadata = (object?)null });
        var prompt = await createResponse.Content.ReadFromJsonAsync<PromptResponse>();

        var response = await _client.PostAsJsonAsync($"/prompts/{prompt!.Id}/versions", new { content = "", createdBy = "alice" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Activate_Returns_Ok_For_A_Valid_Draft_Version()
    {
        var (promptId, versionId) = await SeedVersionAsync();

        var response = await _client.PostAsync($"/prompts/{promptId}/versions/{versionId}/activate", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Activate_Is_Idempotent_When_Called_Again_On_The_Same_Now_Active_Version()
    {
        var (promptId, versionId) = await SeedVersionAsync();
        await _client.PostAsync($"/prompts/{promptId}/versions/{versionId}/activate", content: null);

        var response = await _client.PostAsync($"/prompts/{promptId}/versions/{versionId}/activate", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Activate_Returns_NotFound_For_An_Unknown_Prompt()
    {
        var response = await _client.PostAsync($"/prompts/{Guid.NewGuid()}/versions/{Guid.NewGuid()}/activate", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Activate_Returns_NotFound_For_An_Unknown_Version()
    {
        var (promptId, _) = await SeedVersionAsync();

        var response = await _client.PostAsync($"/prompts/{promptId}/versions/{Guid.NewGuid()}/activate", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
        GC.SuppressFinalize(this);
    }

    private sealed record PromptResponse(Guid Id, string Name, DateTimeOffset CreatedAt);

    private sealed record PromptSummaryResponse(Guid Id, string Name);

    private sealed record PromptMetadataDto(string? Description, IReadOnlyList<string>? Tags, IReadOnlyList<string>? Categories, IReadOnlyList<string>? Owners, IReadOnlyList<string>? ExternalRefs);

    private sealed record PromptMetadataResponse(Guid Id, string Name, PromptMetadataDto Metadata);

    private sealed record PromptVersionResponse(Guid Id, Guid PromptId, int VersionNumber, string Content, string? ChangelogEntry, PromptVersionStatus Status, DateTimeOffset CreatedAt);

    private sealed record PromptVersionDetailResponse(Guid PromptId, string PromptName, Guid VersionId, int VersionNumber, string Content, PromptVersionStatus Status, IReadOnlyList<string> Tags);
}
