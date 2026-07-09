using System.ComponentModel;
using System.Reflection;
using ModelContextProtocol.Server;

namespace PromptOps.Host.Mcp;

/// <summary>
/// The daemon's initial MCP tool set (ADR-0006, Phase 4a): just enough for a client to confirm it
/// can reach the daemon over HTTP. Real tools (search history, get a recommendation, submit a
/// rating) land in later phases as the use cases behind them are built.
/// </summary>
[McpServerToolType]
public static class DaemonTools
{
    [McpServerTool(Name = "health_check"), Description("Reports whether the PromptOps daemon is up and reachable.")]
    public static string HealthCheck() => "ok";

    [McpServerTool(Name = "version"), Description("Reports the running PromptOps daemon's version.")]
    public static string Version() =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0-dev";
}
