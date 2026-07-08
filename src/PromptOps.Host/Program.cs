using PromptOps.Application;
using PromptOps.Infrastructure;
using PromptOps.Plugin.Sdk;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPromptOpsApplication();
builder.Services.AddPromptOpsInfrastructure();

// Plugin discovery/loading (ADR-0004) is stubbed until Phase 5 — the daemon boots with an
// empty plugin set today. Once real, this becomes a scan of the configured plugins directory.
IReadOnlyList<IPromptOpsPlugin> plugins = [];
foreach (var plugin in plugins)
{
    plugin.Register(builder.Services, builder.Configuration);
}

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new HealthResponse("ok", plugins.Count)));

app.Run();

internal sealed record HealthResponse(string Status, int PluginsLoaded);

// Exposed for WebApplicationFactory-based integration tests.
public partial class Program;
