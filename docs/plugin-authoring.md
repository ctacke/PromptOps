# Plugin Authoring

This doc covers writing PromptOps provider plugins. It's still a stub for that subject until Phase 5 fills it in with a worked example (`SonarMetricCollector`); the hook contract for the separate, per-repo Claude Code plugin (Phase 4b) is filled in below. For now, here's the distinction that matters:

## Two different things are called "plugin" in this project — don't confuse them

1. **Daemon-side provider plugins** (this doc's actual subject). A separate .NET assembly that implements `IPromptOpsPlugin` (defined in `plugins/PromptOps.Plugin.Sdk`) and registers one or more of the provider interfaces from ADR-0003 (`IMetricCollector`, `IContextProvider`, `IAIExecutionProvider`, etc.) into the daemon's DI container. This is how PromptOps integrates with Sonar, Jira, GitHub, Claude Code, and so on, without the core (`Domain`/`Application`) ever knowing those tools exist (ADR-0002). Loaded from the daemon's plugins directory — real discovery/loading lands in Phase 5 (ADR-0004); it's a hardcoded empty list today.

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

`Register` is where a plugin adds its provider implementations (e.g. `services.AddSingleton<IMetricCollector, SonarMetricCollector>()`) using whatever configuration section the daemon hands it. There's nothing to build against yet beyond this interface — the provider interfaces themselves (`docs/architecture.md` ADR-0003) are still empty contracts, filled in phase by phase as the use cases that need them are built.

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

Each hook runs as a separate OS process with no shared memory, so anything that needs to survive from `SessionStart` to `SessionEnd` (the execution id, the starting commit) is written to `${CLAUDE_PLUGIN_DATA}/state/<session_id>.json` (falls back to the OS temp dir if `CLAUDE_PLUGIN_DATA` isn't set). Per-tool-call timers live under `state/tools/<session_id>/<tool_use_id>.json` and are deleted by `PostToolUse` once consumed. See `claude-plugin/hooks/lib/state.mjs`.

### MCP registration

`claude-plugin/.mcp.json` (referenced from `plugin.json`'s `mcpServers` field) registers the daemon as a remote HTTP MCP server at `http://127.0.0.1:5179/mcp`. This happens automatically on `claude plugin install` — no separate `claude mcp add` step, unlike the original ADR-0006 draft assumed.
