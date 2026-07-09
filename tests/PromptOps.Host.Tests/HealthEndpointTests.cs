using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Xunit;

namespace PromptOps.Host.Tests;

/// <summary>Uses its own temp SQLite file (not the dev-default <c>promptops.db</c>) so test runs never touch or leave behind real local state.</summary>
public class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"promptops-host-tests-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<Program> _factory;

    public HealthEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:PromptOps", $"Data Source={_dbPath}"));
    }

    [Fact]
    public async Task Daemon_Starts_With_Zero_Plugins_Loaded()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<HealthResponse>();
        Assert.NotNull(body);
        Assert.Equal("ok", body!.Status);
        Assert.Equal(0, body.PluginsLoaded);
    }

    public void Dispose()
    {
        // The Host's request-scoped DbContext releases its connection back to the pool once the
        // request completes, but the pool itself isn't cleared until told to — clear it before
        // deleting or the file delete can lose a race against the pooled handle on Windows.
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
        GC.SuppressFinalize(this);
    }

    private sealed record HealthResponse(string Status, int PluginsLoaded);
}
