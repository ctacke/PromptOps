#!/usr/bin/env node
// SessionEnd finalizes the ExecutionRecord opened at SessionStart.
//
// The project plan (docs/project-plan.md, Phase 4b) names `Stop` as the hook that computes diff
// stats and calls "finish execution". Claude Code's `Stop` event fires once per conversational
// turn, not once per session (see docs/hooks reference: "once per turn: UserPromptSubmit, Stop,
// and StopFailure") — calling Finish on every turn would violate ExecutionRecord's
// InProgress -> Finished transition, which is only valid once (docs/execution-tracking.md).
// `SessionEnd` is the event that actually fires once, when the session ends, so that's what's
// wired here instead. Tool usage is still recorded continuously across every turn via
// PreToolUse/PostToolUse, so nothing is lost by not finishing on every `Stop`.
import { daemonUrl, executionStatePath, fetchWithTimeout, readJson } from "./lib/state.mjs";
import { diffStats } from "./lib/git.mjs";

const raw = [];
for await (const chunk of process.stdin) raw.push(chunk);
const input = raw.length ? JSON.parse(Buffer.concat(raw).toString("utf8")) : {};
const { session_id: sessionId, cwd } = input;

if (!sessionId) process.exit(0);

const execution = readJson(executionStatePath(sessionId));
if (!execution) process.exit(0); // no active execution (daemon was down at SessionStart) — no-op

const { filesChanged, linesAdded, linesDeleted } = diffStats(cwd || execution.cwd, execution.startedAtCommit);

try {
  await fetchWithTimeout(
    `${daemonUrl()}/executions/${execution.executionId}/finish`,
    {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        executionTimeMs: execution.startedAtMs ? Date.now() - execution.startedAtMs : 0,
        aiProviderId: "claude-code",
        filesChanged,
        linesAdded,
        linesDeleted
      })
    },
    4000
  );
} catch {
  // Best-effort: SessionEnd hooks can't block session termination anyway.
}

process.exit(0);
