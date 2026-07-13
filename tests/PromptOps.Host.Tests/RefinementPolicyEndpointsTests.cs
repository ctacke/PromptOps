using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Xunit;

namespace PromptOps.Host.Tests;

/// <summary>End-to-end over real HTTP for Phase 16's <c>/refinement-policy</c> singleton — GET lazily bootstraps the default (off), PUT toggles it.</summary>
public class RefinementPolicyEndpointsTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"promptops-refinement-policy-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public RefinementPolicyEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:PromptOps", $"Data Source={_dbPath}"));
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task Get_Returns_The_Default_Policy_With_Refinement_And_Benchmarking_Off()
    {
        var policy = await _client.GetFromJsonAsync<RefinementPolicyResponse>("/refinement-policy");

        Assert.NotNull(policy);
        Assert.False(policy!.AutoRefinementEnabled);
        Assert.Equal(0, policy.SyntheticSampleSize);
        Assert.Equal(0, policy.MinQualityDelta);
        Assert.Equal(0, policy.AbExplorationRate);
    }

    [Fact]
    public async Task Put_Configures_The_Policy_And_Is_Reflected_On_Get()
    {
        var putResponse = await _client.PutAsJsonAsync("/refinement-policy",
            new { autoRefinementEnabled = true, syntheticSampleSize = 5, minQualityDelta = 3.0, abExplorationRate = 0.2 });
        putResponse.EnsureSuccessStatusCode();
        var updated = await putResponse.Content.ReadFromJsonAsync<RefinementPolicyResponse>();
        Assert.True(updated!.AutoRefinementEnabled);
        Assert.Equal(5, updated.SyntheticSampleSize);
        Assert.Equal(3.0, updated.MinQualityDelta);
        Assert.Equal(0.2, updated.AbExplorationRate);

        var reloaded = await _client.GetFromJsonAsync<RefinementPolicyResponse>("/refinement-policy");
        Assert.True(reloaded!.AutoRefinementEnabled);
        Assert.Equal(5, reloaded.SyntheticSampleSize);
        Assert.Equal(0.2, reloaded.AbExplorationRate);
    }

    [Theory]
    [InlineData(-1, 0.0, 0.0)]
    [InlineData(0, 0.0, 1.5)]
    public async Task Put_Rejects_Out_Of_Range_Settings(int sampleSize, double minDelta, double explorationRate)
    {
        var response = await _client.PutAsJsonAsync("/refinement-policy",
            new { autoRefinementEnabled = true, syntheticSampleSize = sampleSize, minQualityDelta = minDelta, abExplorationRate = explorationRate });

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
        GC.SuppressFinalize(this);
    }

    private sealed record RefinementPolicyResponse(Guid Id, bool AutoRefinementEnabled, int SyntheticSampleSize, double MinQualityDelta, double AbExplorationRate, DateTimeOffset UpdatedAt);
}
