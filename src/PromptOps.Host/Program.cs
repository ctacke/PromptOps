using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PromptOps.Application;
using PromptOps.Host.Endpoints;
using PromptOps.Host.Plugins;
using PromptOps.Infrastructure;
using PromptOps.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPromptOpsApplication();
builder.Services.AddPromptOpsInfrastructure(builder.Configuration);
builder.Services.AddMcpServer().WithHttpTransport().WithToolsFromAssembly();

// Plugin discovery/loading (ADR-0004): scans the configured directory (default: "plugins" next to
// the daemon's own binaries — /app/plugins inside the Docker image) for daemon-side provider
// plugins and registers each one's services before the container is built.
var pluginsDirectory = builder.Configuration["Plugins:Directory"] ?? Path.Combine(AppContext.BaseDirectory, "plugins");
using var bootstrapLoggerFactory = LoggerFactory.Create(logging => logging.AddConsole());
var plugins = PluginLoader.LoadAndRegister(
    builder.Services, builder.Configuration, pluginsDirectory, bootstrapLoggerFactory.CreateLogger("PluginLoader"));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<PromptOpsDbContext>().Database.MigrateAsync();
}

app.MapGet("/health", () => Results.Ok(new HealthResponse("ok", plugins.Count)));
app.MapExecutionEndpoints();
app.MapMetricsEndpoints();
app.MapEvaluationEndpoints();
app.MapAIEvaluationEndpoints();
app.MapScoringEndpoints();
app.MapRecommendationEndpoints();
app.MapPromptEndpoints();
app.MapPromotionPolicyEndpoints();
app.MapAIEvaluationPolicyEndpoints();
app.MapStatisticsEndpoints();
app.MapDashboardEndpoints();
app.MapMcp("/mcp");

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");

app.Run();

internal sealed record HealthResponse(string Status, int PluginsLoaded);

// Exposed for WebApplicationFactory-based integration tests.
public partial class Program;
