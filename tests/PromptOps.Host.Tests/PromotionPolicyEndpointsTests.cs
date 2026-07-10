using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Xunit;

namespace PromptOps.Host.Tests;

public class PromotionPolicyEndpointsTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"promptops-promotion-policy-endpoints-{Guid.NewGuid():N}.db");
    private readonly HttpClient _client;

    public PromotionPolicyEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:PromptOps", $"Data Source={_dbPath}")).CreateClient();
    }

    [Fact]
    public async Task Get_Lazily_Bootstraps_The_Default_Policy_On_A_Fresh_Daemon()
    {
        var response = await _client.GetAsync("/promotion-policy");

        var policy = await response.Content.ReadFromJsonAsync<PromotionPolicyResponse>();
        Assert.True(policy!.RequireHumanEvaluation);
        Assert.False(policy.AutoPromotionEnabled);
    }

    [Fact]
    public async Task Put_Updates_The_Policy_And_The_Change_Is_Visible_On_A_Subsequent_Get()
    {
        var putResponse = await _client.PutAsJsonAsync("/promotion-policy", new
        {
            requireHumanEvaluation = false,
            autoPromotionEnabled = true,
            minimumScoreThreshold = 85.0,
            minimumMarginOverActive = (double?)null
        });
        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);

        var getResponse = await _client.GetAsync("/promotion-policy");
        var policy = await getResponse.Content.ReadFromJsonAsync<PromotionPolicyResponse>();

        Assert.False(policy!.RequireHumanEvaluation);
        Assert.True(policy.AutoPromotionEnabled);
        Assert.Equal(85.0, policy.MinimumScoreThreshold);
    }

    [Fact]
    public async Task Put_Rejects_Enabling_Auto_Promotion_While_Human_Evaluation_Is_Still_Required()
    {
        var response = await _client.PutAsJsonAsync("/promotion-policy", new
        {
            requireHumanEvaluation = true,
            autoPromotionEnabled = true,
            minimumScoreThreshold = 85.0,
            minimumMarginOverActive = (double?)null
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_Rejects_Enabling_Auto_Promotion_With_Neither_Threshold_Nor_Margin()
    {
        var response = await _client.PutAsJsonAsync("/promotion-policy", new
        {
            requireHumanEvaluation = false,
            autoPromotionEnabled = true,
            minimumScoreThreshold = (double?)null,
            minimumMarginOverActive = (double?)null
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
        GC.SuppressFinalize(this);
    }

    private sealed record PromotionPolicyResponse(
        Guid Id, bool RequireHumanEvaluation, bool AutoPromotionEnabled,
        double? MinimumScoreThreshold, double? MinimumMarginOverActive, DateTimeOffset UpdatedAt);
}
