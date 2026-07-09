using Microsoft.EntityFrameworkCore;
using PromptOps.Application;
using PromptOps.Host.Endpoints;
using PromptOps.Infrastructure;
using PromptOps.Infrastructure.Persistence;
using PromptOps.Plugin.Sdk;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPromptOpsApplication();
builder.Services.AddPromptOpsInfrastructure(builder.Configuration);

// Plugin discovery/loading (ADR-0004) is stubbed until Phase 5 — the daemon boots with an
// empty plugin set today. Once real, this becomes a scan of the configured plugins directory.
IReadOnlyList<IPromptOpsPlugin> plugins = [];
foreach (var plugin in plugins)
{
    plugin.Register(builder.Services, builder.Configuration);
}

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<PromptOpsDbContext>().Database.MigrateAsync();
}

app.MapGet("/health", () => Results.Ok(new HealthResponse("ok", plugins.Count)));
app.MapExecutionEndpoints();

app.Run();

internal sealed record HealthResponse(string Status, int PluginsLoaded);

// Exposed for WebApplicationFactory-based integration tests.
public partial class Program;
