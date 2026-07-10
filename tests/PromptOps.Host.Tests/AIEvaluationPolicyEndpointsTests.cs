using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Xunit;

namespace PromptOps.Host.Tests;

public class AIEvaluationPolicyEndpointsTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"promptops-ai-evaluation-policy-endpoints-{Guid.NewGuid():N}.db");
    private readonly HttpClient _client;

    public AIEvaluationPolicyEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:PromptOps", $"Data Source={_dbPath}")).CreateClient();
    }

    [Fact]
    public async Task Get_Lazily_Bootstraps_The_Default_Policy_On_A_Fresh_Daemon()
    {
        var response = await _client.GetAsync("/ai-evaluation-policy");

        var policy = await response.Content.ReadFromJsonAsync<AIEvaluationPolicyResponse>();
        Assert.False(policy!.AutoEvaluateOnFinish);
        Assert.Equal("Daemon", policy.Mechanism);
    }

    [Fact]
    public async Task Put_Updates_The_Policy_And_The_Change_Is_Visible_On_A_Subsequent_Get()
    {
        var putResponse = await _client.PutAsJsonAsync("/ai-evaluation-policy", new { autoEvaluateOnFinish = true });
        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);

        var getResponse = await _client.GetAsync("/ai-evaluation-policy");
        var policy = await getResponse.Content.ReadFromJsonAsync<AIEvaluationPolicyResponse>();

        Assert.True(policy!.AutoEvaluateOnFinish);
    }

    [Fact]
    public async Task Put_Omitting_Mechanism_Defaults_To_Daemon()
    {
        var putResponse = await _client.PutAsJsonAsync("/ai-evaluation-policy", new { autoEvaluateOnFinish = true });

        var policy = await putResponse.Content.ReadFromJsonAsync<AIEvaluationPolicyResponse>();
        Assert.Equal("Daemon", policy!.Mechanism);
    }

    [Fact]
    public async Task Put_Can_Set_Mechanism_To_ClientHook()
    {
        var putResponse = await _client.PutAsJsonAsync("/ai-evaluation-policy", new { autoEvaluateOnFinish = true, mechanism = "ClientHook" });
        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);

        var policy = await putResponse.Content.ReadFromJsonAsync<AIEvaluationPolicyResponse>();
        Assert.Equal("ClientHook", policy!.Mechanism);

        var getResponse = await _client.GetAsync("/ai-evaluation-policy");
        var reGet = await getResponse.Content.ReadFromJsonAsync<AIEvaluationPolicyResponse>();
        Assert.Equal("ClientHook", reGet!.Mechanism);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
        GC.SuppressFinalize(this);
    }

    private sealed record AIEvaluationPolicyResponse(Guid Id, bool AutoEvaluateOnFinish, string Mechanism, DateTimeOffset UpdatedAt);
}
