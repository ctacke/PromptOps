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
  UNTRACKED_PROMPT_VERSION_ID
} from "./lib/state.mjs";
import { getRepository, getBranch, getCommit, getDeveloperId, detectLanguages } from "./lib/git.mjs";

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

const startedAtCommit = getCommit(cwd);

let executionId = null;
try {
  const response = await fetchWithTimeout(
    `${daemonUrl()}/executions/start`,
    {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        promptVersionId: UNTRACKED_PROMPT_VERSION_ID,
        developerId: getDeveloperId(cwd),
        repository: getRepository(cwd),
        branch: getBranch(cwd),
        commit: startedAtCommit,
        languages: detectLanguages(cwd)
      })
    },
    3000
  );
  if (response.ok) {
    const body = await response.json();
    executionId = body.executionId;
  }
} catch {
  executionId = null;
}

if (!executionId) {
  emitHookOutput("SessionStart", {
    additionalContext: "PromptOps: the daemon is reachable but starting an execution failed. Tool usage and diff stats will not be recorded for this session."
  });
  process.exit(0);
}

writeJson(executionStatePath(sessionId), { executionId, startedAtCommit, cwd, startedAtMs: Date.now() });
process.exit(0);
