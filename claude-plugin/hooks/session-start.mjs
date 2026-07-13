#!/usr/bin/env node
// SessionStart (ADR-0006 push model): gathers repo/branch/commit locally — the daemon has no
// filesystem access to any repo — and asks the daemon to open an ExecutionRecord.
import {
  daemonUrl,
  executionStatePath,
  fetchWithTimeout,
  readStdinJson,
  emitHookOutput,
  writeJson,
  readJson,
  removeFile
} from "./lib/state.mjs";
import { getCommit, diffStats } from "./lib/git.mjs";

const input = await readStdinJson();
const { session_id: sessionId, cwd } = input;

if (!sessionId || !cwd) process.exit(0);

let health;
try {
  health = await fetchWithTimeout(`${daemonUrl()}/health`, {}, 2000);
} catch {
  health = null;
}

if (!health || !health.ok) {
  emitHookOutput("SessionStart", {
    additionalContext:
      "The PromptOps daemon is not reachable at " + daemonUrl() + ". Execution tracking for this " +
      "session is disabled until it's running. Offer to start it: use the promptops setup skill, " +
      "or run `docker run -d --name promptops-daemon -p 127.0.0.1:5179:8080 -v promptops-data:/data ghcr.io/ctacke/promptops:latest`. " +
      "See docs/daemon-setup.md / docs/installing-promptops.md."
  });
  process.exit(0);
}

// Finalize any execution left dangling by this session_id — /clear, a crash, or anything else
// that reaches SessionStart again without SessionEnd having run first. /clear itself isn't a
// hook event this plugin can observe directly; this is a general safety net instead, so it
// self-heals regardless of what caused SessionStart to fire again while an execution was open.
const stale = readJson(executionStatePath(sessionId));
if (stale && stale.executionId) {
  try {
    const { filesChanged, linesAdded, linesDeleted } = diffStats(cwd, stale.startedAtCommit);
    const finishResponse = await fetchWithTimeout(
      `${daemonUrl()}/executions/${stale.executionId}/finish`,
      {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          executionTimeMs: stale.startedAtMs ? Date.now() - stale.startedAtMs : 0,
          aiProviderId: "claude-code",
          filesChanged,
          linesAdded,
          linesDeleted
        })
      },
      4000
    );
    if (finishResponse.ok) removeFile(executionStatePath(sessionId));
  } catch {
    // Best-effort — leave the stale file in place so the *next* SessionStart retries
    // finalizing it, rather than losing the record silently on a daemon hiccup.
  }
}

// Phase 15: don't open the ExecutionRecord here. It can't be attributed to a PromptVersion until
// the developer's first prompt exists (and ExecutionRecord.PromptVersionId is immutable once the
// record is created — see ExecutionAttributionService), so opening it at SessionStart is what forced
// the old "untracked" placeholder. Instead, record a *pre-open* state file: it captures the diff
// baseline (startedAtCommit) from the very start of the session, and the UserPromptSubmit hook then
// opens the attributed execution on the first turn and writes executionId back into this file.
writeJson(executionStatePath(sessionId), { startedAtCommit: getCommit(cwd), cwd, startedAtMs: Date.now() });
process.exit(0);
