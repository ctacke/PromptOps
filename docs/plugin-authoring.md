# Plugin Authoring

This doc covers writing PromptOps provider plugins, with `SonarMetricCollector` (Phase 5) as the worked example. The hook contract for the separate, per-repo Claude Code plugin (Phase 4b) is further below. Here's the distinction that matters:

## Two different things are called "plugin" in this project — don't confuse them

1. **Daemon-side provider plugins** (this doc's actual subject). A separate .NET assembly that implements `IPromptOpsPlugin` (defined in `plugins/PromptOps.Plugin.Sdk`) and registers one or more of the provider interfaces from ADR-0003 (`IMetricCollector`, `IContextProvider`, `IAIExecutionProvider`, etc.) into the daemon's DI container. This is how PromptOps integrates with Sonar, Jira, GitHub, Claude Code, and so on, without the core (`Domain`/`Application`) ever knowing those tools exist (ADR-0002). Discovery/loading (ADR-0004) is real as of Phase 5 — see below.

2. **The per-repo Claude Code plugin** (a different artifact entirely — see ADR-0009). This is what actually gets installed into a target repository: a `.claude-plugin/plugin.json` manifest, Node.js hook scripts, and skills, with no compiled/.NET code of its own. It talks to the daemon over `localhost`. Lives in `claude-plugin/` at the repo root. Built in Phase 4b.

If you're looking for "how do I get PromptOps working in my repo," that's #2 (`docs/installing-promptops.md`). If you're extending what the daemon can measure or connect to, that's #1 — the rest of this doc.

## `IPromptOpsPlugin` (current shape)

```csharp
public interface IPromptOpsPlugin
{
    string Name { get; }
    string Version { get; }
    void Register(IServiceCollection services, IConfiguration configuration);
}
```

`Register` is where a plugin adds its provider implementations using whatever configuration section the daemon hands it.

## Worked example: `SonarMetricCollector` (Phase 5)

A minimal, complete daemon-side plugin, in `plugins/PromptOps.Plugins.Sonar/`:

```
plugins/PromptOps.Plugins.Sonar/
├── PromptOps.Plugins.Sonar.csproj   references PromptOps.Application + PromptOps.Plugin.Sdk only
├── SonarPlugin.cs                   IPromptOpsPlugin — the Register() entry point
├── SonarMetricCollector.cs          IMetricCollector — the actual work
├── SonarOptions.cs                  bound from configuration
└── SonarMeasuresResponse.cs         DTOs for Sonar's measures API response
```

```csharp
// SonarPlugin.cs
public sealed class SonarPlugin : IPromptOpsPlugin
{
    public string Name => "sonar";
    public string Version => "0.5.0";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SonarOptions>(configuration);
        services.AddHttpClient<SonarMetricCollector>();
        services.AddScoped<IMetricCollector>(sp => sp.GetRequiredService<SonarMetricCollector>());
    }
}
```

`configuration` here is already scoped to `Plugins:sonar` by `PluginLoader` (see below) — the plugin never needs to know its own name is baked into a config path. `SonarMetricCollector` itself takes `HttpClient`, `IExecutionRepository` (to resolve the execution's `Context.Repository` into a Sonar project key), `ISecretProvider` (to resolve the auth token — never from plain config, ADR-0007), and `IOptions<SonarOptions>` — all constructor-injected, all from `Application`, nothing from `Infrastructure`. See `docs/metrics.md` for what it actually does with them.

Following this pattern for a new collector — say, a review-metrics plugin reading GitHub PR data — means: a new project under `plugins/`, an `IPromptOpsPlugin` + `IMetricCollector` pair, a `ProjectReference` to `PromptOps.Application` and `PromptOps.Plugin.Sdk`, and one new line in the Dockerfile's publish step (below). Nothing in `Domain`, `Application`, `Host`, or any *other* plugin changes.

## Plugin discovery and loading (ADR-0004)

`PromptOps.Host.Plugins.PluginLoader.LoadAndRegister` scans `Plugins:Directory` (default: `plugins/` next to the daemon's own binaries — `/app/plugins` in the Docker image) for subdirectories. For each one, it expects the primary DLL at `{pluginsDirectory}/{FolderName}/{FolderName}.dll` — convention over a separate manifest file, since the folder layout already says everything a manifest would. That's exactly what `dotnet publish -o /app/plugins/PromptOps.Plugins.Sonar` produces, which is what the Dockerfile does for each in-tree plugin project.

Each plugin's DLL is loaded into its **own** `AssemblyLoadContext` (`PluginLoadContext`) — a broken or incompatible plugin can't take down the daemon process. This isolation has one real trap, worth understanding before writing a new plugin that references anything beyond `PromptOps.Application`/`PromptOps.Plugin.Sdk`:

> **The type-identity trap.** `IPromptOpsPlugin.Register`'s signature is `(IServiceCollection, IConfiguration)`. If a plugin's isolated `AssemblyLoadContext` loads its *own* copy of `Microsoft.Extensions.DependencyInjection.Abstractions` (a plain NuGet package, not part of the ASP.NET Core shared framework — so nothing stops it being loaded twice) instead of reusing the daemon's own copy, the plugin's `IServiceCollection` parameter type is no longer *the same type* as the host's, even though it has the same name. The CLR can't build the plugin type's method table and throws a `TypeLoadException` reading "Method 'Register' does not have an implementation" — a confusing message, since the method is right there in source; the type it takes just doesn't match across the two copies of the assembly that declares it. `PluginLoadContext.Load` avoids this by explicitly handing back the exact `Assembly` instance already loaded in `AssemblyLoadContext.Default` for `PromptOps.Domain`/`PromptOps.Application`/`PromptOps.Plugin.Sdk` and everything under `Microsoft.Extensions.*`/`Microsoft.AspNetCore*`/`System.*` — sharing identity, not just resolving a same-named assembly. Only a plugin's genuinely private dependencies (resolved via `AssemblyDependencyResolver`, reading the plugin's own `.deps.json`) get loaded in isolation. If you add a plugin with a private third-party dependency and hit this exact exception, it means that dependency's own transitive references pulled in something that should have been on the shared list.

Adding or removing a plugin is a **config change**: drop a new `{Name}/{Name}.dll` folder under the plugins directory (or delete one) and restart the daemon. `MetricsCollectionService` and the ingestion endpoints never change — they resolve `IEnumerable<IMetricCollector>` from DI and iterate whatever's there.

## The Claude Code plugin's hook contract (Phase 4b)

`claude-plugin/hooks/hooks.json` registers four hooks, all `type: "command"`, all Node.js scripts run as `node "${CLAUDE_PLUGIN_ROOT}/hooks/<name>.mjs"` (Node was chosen over plain shell + `jq` for cross-platform reliability — Windows Git Bash has no `jq` by default, and this matches the convention other installed plugins in this environment already use). Each script reads one JSON object from stdin and exits 0 unconditionally — none of these hooks are meant to block anything, so a daemon outage degrades to "tracking is off for this session," never to a blocked tool call or session.

| Hook | Fires | Does |
|---|---|---|
| `SessionStart` | Once, at session start/resume | Checks `GET /health`; if up, gathers `repository`/`branch`/`commit`/`languages` via local `git` and calls `POST /executions/start`; caches the returned execution id keyed by `session_id` under `${CLAUDE_PLUGIN_DATA}/state/`. If the daemon is down, injects `additionalContext` telling Claude to offer the `setup` skill instead of failing silently. |
| `PreToolUse` | Every tool call, before it runs | Writes `{ toolName, startedAtMs }` to a per-`tool_use_id` timer file. No network call — stays fast. |
| `PostToolUse` | Every tool call, after it completes | Reads the matching timer file, computes a real duration, calls `POST /executions/{id}/tool-usage`. No-ops if `SessionStart` never got an execution id (daemon was down). |
| `SessionEnd` | Once, when the session actually ends | Computes `git diff --numstat <commit-at-SessionStart>` against the current working tree, calls `POST /executions/{id}/finish` with files changed / lines added / lines deleted and elapsed wall-clock time. |

### Why `SessionEnd`, not `Stop`

The original Phase 4 plan named `Stop` as the hook that computes diff stats and finishes the execution. Claude Code's `Stop` event fires once per conversational turn, not once per session (it's in the same cadence bucket as `UserPromptSubmit`). `ExecutionRecord.Finish` is a one-way `InProgress → Finished` transition (`docs/execution-tracking.md`) — calling it on every turn would fail on the second turn onward. `SessionEnd` is the event that genuinely fires once, when the session ends, so that's what's wired to "finish execution" instead. Tool usage tracking is unaffected by this — `PreToolUse`/`PostToolUse` fire correctly across every turn regardless.

### State across hook invocations

Each hook runs as a separate OS process with no shared memory, so anything that needs to survive from `SessionStart` onward (the execution id, the starting commit) is written to `${CLAUDE_PLUGIN_DATA}/state/<session_id>.json` (falls back to the OS temp dir if `CLAUDE_PLUGIN_DATA` isn't set). Per-tool-call timers live under `state/tools/<session_id>/<tool_use_id>.json` and are deleted by `PostToolUse` once consumed. See `claude-plugin/hooks/lib/state.mjs`.

The execution state file's existence *is* the "is this execution still open" signal: `SessionEnd` deletes it only after a successful `/executions/{id}/finish`, and `SessionStart` checks for it on every invocation — if one already exists for this `session_id`, the previous execution was never properly finished (`/clear`, a crash, anything short of a normal `/exit`), so `SessionStart` finalizes it itself (real diff stats against the commit recorded when it opened) before starting the new one. This is what keeps an execution from accumulating tool usage indefinitely if a session never reaches a clean `SessionEnd`.

### MCP registration

`claude-plugin/.mcp.json` (referenced from `plugin.json`'s `mcpServers` field) registers the daemon as a remote HTTP MCP server at `http://127.0.0.1:5179/mcp`. This happens automatically on `claude plugin install` — no separate `claude mcp add` step, unlike the original ADR-0006 draft assumed.
