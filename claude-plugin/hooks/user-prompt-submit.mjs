#!/usr/bin/env node
// UserPromptSubmit (Phase 15): opens the session's ExecutionRecord on the FIRST prompt, attributed
// to a real PromptVersion instead of the all-zeros "untracked" placeholder.
//
// Why here and not SessionStart: attribution needs the task description (the user's prompt), which
// doesn't exist yet at SessionStart, and ExecutionRecord.PromptVersionId is immutable once the
// record is created — so the record can't be opened until we know what to attribute it to. The
// daemon classifies the prompt and either (a) attributes it to an existing prompt for that activity
// (surfacing that prompt's content in-session), (b) captures a new prompt for a novel development
// activity, or (c) leaves it untracked for non-development chatter.
//
// Runs only on the first turn (when the pre-open state file has no executionId yet); later turns are
// a no-op. Best-effort: any daemon problem falls back to opening a plain untracked execution so
// tool-usage/diff tracking still works, exactly as before Phase 15.
import {
  daemonUrl,
  executionStatePath,
  fetchWithTimeout,
  readStdinJson,
  emitHookOutput,
  writeJson,
  readJson,
  UNTRACKED_PROMPT_VERSION_ID
} from "./lib/state.mjs";
import { getRepository, getBranch, getDeveloperId, detectLanguages } from "./lib/git.mjs";

const input = await readStdinJson();
const { session_id: sessionId, prompt } = input;
const cwd = input.cwd;

if (!sessionId || !prompt) process.exit(0);

const state = readJson(executionStatePath(sessionId));
// No pre-open state → daemon was unreachable at SessionStart (it already told the user), so there's
// nothing to open. An executionId already present → not the first turn, so attribution is done.
if (!state || state.executionId) process.exit(0);

const workingDir = cwd || state.cwd;

async function openUntracked() {
  try {
    const response = await fetchWithTimeout(
      `${daemonUrl()}/executions/start`,
      {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          promptVersionId: UNTRACKED_PROMPT_VERSION_ID,
          developerId: getDeveloperId(workingDir),
          repository: getRepository(workingDir),
          branch: getBranch(workingDir),
          commit: state.startedAtCommit,
          languages: detectLanguages(workingDir)
        })
      },
      3000
    );
    if (response.ok) {
      const body = await response.json();
      writeJson(executionStatePath(sessionId), { ...state, executionId: body.executionId });
    }
  } catch {
    // Best-effort — a future SessionStart's stale check won't fire without an executionId, so on
    // total failure this session simply goes unrecorded rather than leaving a half-open record.
  }
}

let attributed = null;
try {
  const response = await fetchWithTimeout(
    `${daemonUrl()}/executions/start-attributed`,
    {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        prompt,
        developerId: getDeveloperId(workingDir),
        repository: getRepository(workingDir),
        branch: getBranch(workingDir),
        commit: state.startedAtCommit,
        languages: detectLanguages(workingDir)
      })
    },
    // Generous: this includes a daemon-side classification model call, and it runs once per session.
    // If it overruns, the fallback keeps tracking working without the attribution/recommendation.
    15000
  );
  if (response.ok) attributed = await response.json();
} catch {
  attributed = null;
}

if (!attributed) {
  await openUntracked();
  process.exit(0);
}

writeJson(executionStatePath(sessionId), { ...state, executionId: attributed.executionId });

// Surface the chosen prompt in-session so the agent naturally uses it — no slash command, no
// leaving the session (the project's core UX goal).
if (attributed.attribution === "recommended" && attributed.content) {
  emitHookOutput("UserPromptSubmit", {
    additionalContext:
      `PromptOps recommends a proven prompt for this task. ${attributed.rationale ?? ""}\n` +
      `Consider using or adapting it:\n\n---\n${attributed.content}\n---`
  });
} else if (attributed.attribution === "captured" && attributed.rationale) {
  emitHookOutput("UserPromptSubmit", { additionalContext: `PromptOps: ${attributed.rationale}` });
}

process.exit(0);
