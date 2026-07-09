# Installing PromptOps into a repo (Phase 4b)

This is the actual "copy/install into a repo" flow the whole project was built to produce (see `README.md` and ADR-0009). It has two parts, done in this order:

1. **Start the daemon once per machine** — see `docs/daemon-setup.md`. Skip this if it's already running (`curl http://127.0.0.1:5179/health`).
2. **Install the thin Claude Code plugin into each repo** you want tracked — this doc.

## Install the plugin

From inside the target repo, in a Claude Code session:

```
claude plugin marketplace add ctacke/PromptOps
claude plugin install promptops@promptops
```

This is the same mechanism used to install `context-mode` in this environment: a marketplace reference (here, a GitHub repo) followed by `plugin install <plugin-name>@<marketplace-name>`. `claude plugin marketplace add` accepts a GitHub `owner/repo` shorthand, a full git URL, or a local path — useful for testing an unpublished checkout with `claude plugin marketplace add /path/to/PromptOps`.

No manual config editing is required beyond that — installing the plugin:

- Registers hooks (`SessionStart`, `PreToolUse`, `PostToolUse`, `SessionEnd`) from `claude-plugin/hooks/hooks.json`.
- Registers the daemon as a remote MCP server (`claude-plugin/.mcp.json`, `http://127.0.0.1:5179/mcp`) — no separate `claude mcp add` step.
- Adds the `/promptops:setup`, `/promptops:rate`, `/promptops:recommend`, `/promptops:history` skills.

## What happens when you start a session

1. `SessionStart` fires: checks the daemon is reachable, gathers repo/branch/commit locally via `git` (the daemon has no filesystem access to any repo — ADR-0005 §9), and calls `POST /executions/start`. The returned execution id is cached in `${CLAUDE_PLUGIN_DATA}/state/<session-id>.json` for the rest of the session.

   If the daemon isn't reachable, the hook doesn't block the session — it injects context telling Claude to offer running the `setup` skill instead.

2. Every tool call fires `PreToolUse` (records a start timestamp) and `PostToolUse` (posts one tool-usage entry — name, count, real duration — to `POST /executions/{id}/tool-usage`).
3. `SessionEnd` fires when the session actually ends: computes a `git diff --numstat` between the commit captured at `SessionStart` and the current working tree, and calls `POST /executions/{id}/finish` with files changed / lines added / lines deleted.

See `docs/plugin-authoring.md` for the full hook contract and why `SessionEnd` — not `Stop` — is what finalizes the record, despite the project plan naming `Stop`.

## Verifying it worked

```
curl http://127.0.0.1:5179/executions/<execution-id>
```

should show `"status":"Finished"` with non-empty `filesChanged` after ending a session in which you edited files. `scripts/plugin-smoke-test.ps1` in this repo automates this end-to-end against a scratch repo, without needing a real interactive Claude Code session.

## Known Phase 4b limitations

- `ExecutionRecord.PromptVersionId` is recorded as a placeholder (`00000000-0000-0000-0000-000000000000`) until Phase 6/9 wire up real prompt selection — recording *that work happened* doesn't yet require knowing *which prompt version* drove it.
- `/promptops:rate`, `/promptops:recommend`, `/promptops:history` are wired (they reach the plugin, MCP registration works) but return "not yet implemented" — their backing endpoints land in Phases 6/9.
- If a session ends abnormally (e.g. the terminal is force-killed) rather than via a normal `SessionEnd`, that execution stays `InProgress` in the daemon forever. Not handled in this phase.
