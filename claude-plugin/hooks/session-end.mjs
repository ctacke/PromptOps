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
import { spawn } from "node:child_process";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";
import { daemonUrl, executionStatePath, fetchWithTimeout, readJson, removeFile } from "./lib/state.mjs";
import { diffStats } from "./lib/git.mjs";

const __dirname = dirname(fileURLToPath(import.meta.url));

const raw = [];
for await (const chunk of process.stdin) raw.push(chunk);
const input = raw.length ? JSON.parse(Buffer.concat(raw).toString("utf8")) : {};
const { session_id: sessionId, cwd } = input;

if (!sessionId) process.exit(0);

const execution = readJson(executionStatePath(sessionId));
if (!execution) process.exit(0); // no active execution (daemon was down at SessionStart) — no-op

const { filesChanged, linesAdded, linesDeleted } = diffStats(cwd || execution.cwd, execution.startedAtCommit);

try {
  const response = await fetchWithTimeout(
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
  // Only clear the state file once the finish actually landed — this is what lets a future
  // SessionStart tell "finished" apart from "still open" purely from the file's existence
  // (see session-start.mjs's stale-execution check). On failure, leave it in place so nothing
  // downstream mistakes an unfinished execution for a finished one.
  if (response.ok) removeFile(executionStatePath(sessionId));
} catch {
  // Best-effort: SessionEnd hooks can't block session termination anyway.
}

// Client-side automatic evaluation (Phase 13/ADR-0010 amendment): only when the daemon's policy
// opts into ClientHook instead of its own (Daemon-owned) automatic trigger. Spawned detached so this
// hook's own exit is never delayed by a `claude -p` round trip, which can take many seconds — see
// auto-evaluate.mjs's own comment for why the detach (not just this call) matters.
try {
  const policyResponse = await fetchWithTimeout(`${daemonUrl()}/ai-evaluation-policy`, {}, 3000);
  if (policyResponse.ok) {
    const policy = await policyResponse.json();
    if (policy.autoEvaluateOnFinish && policy.mechanism === "ClientHook") {
      const child = spawn(
        process.execPath,
        [join(__dirname, "auto-evaluate.mjs"), execution.executionId],
        { detached: true, stdio: "ignore" }
      );
      child.unref();
    }
  }
} catch {
  // Best-effort: a policy-check hiccup here must never block session termination either.
}

process.exit(0);
