#!/usr/bin/env node
// PostToolUse: pairs with the PreToolUse timestamp to record one tool-usage entry against the
// session's ExecutionRecord. Best-effort throughout — a daemon hiccup here must never surface as
// a hook error to the user, since the tool call itself already succeeded.
import {
  daemonUrl,
  executionStatePath,
  fetchWithTimeout,
  readJson,
  readStdinJson,
  removeFile,
  toolTimerPath
} from "./lib/state.mjs";

const input = await readStdinJson();
const { session_id: sessionId, tool_use_id: toolUseId, tool_name: toolName } = input;

if (!sessionId || !toolName) process.exit(0);

const execution = readJson(executionStatePath(sessionId));
if (!execution) process.exit(0); // no active execution (daemon was down at SessionStart) — no-op

const timerPath = toolUseId ? toolTimerPath(sessionId, toolUseId) : null;
const timer = timerPath ? readJson(timerPath) : null;
const durationMs = timer ? Date.now() - timer.startedAtMs : 0;

try {
  await fetchWithTimeout(
    `${daemonUrl()}/executions/${execution.executionId}/tool-usage`,
    {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ name: toolName, count: 1, durationMs })
    },
    3000
  );
} catch {
  // Best-effort: a dropped tool-usage record isn't worth surfacing to the user mid-session.
}

if (timerPath) removeFile(timerPath);
process.exit(0);
