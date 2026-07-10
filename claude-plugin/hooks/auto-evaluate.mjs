#!/usr/bin/env node
// Detached child spawned by session-end.mjs when AIEvaluationPolicy is
// { autoEvaluateOnFinish: true, mechanism: "ClientHook" } (Phase 13/ADR-0010 amendment).
//
// Delegates automatic evaluation to the developer's own already-authenticated `claude` CLI instead
// of a daemon-owned IAIExecutionProvider: same client-delegated judge prompt/schema the interactive
// /promptops evaluate skill answers itself (Phase 12), just answered by a headless `claude -p`
// invocation instead of a human's live conversation turn, and triggered unattended on session end
// instead of by a human typing the slash command.
//
// Runs detached from session-end.mjs's own process (see that file's comment on why: a `claude -p`
// round trip can take many seconds and must never hang session termination). Everything here is
// therefore best-effort and silent by construction — there is no session left to surface failures
// to, so they're appended to a local log file instead (docs/ai-evaluation.md).
import { spawn } from "node:child_process";
import { daemonUrl, fetchWithTimeout, logPath, appendLog } from "./lib/state.mjs";

// Matches JudgePromptBuilder.MaxAttempts (PromptOps.Application.Evaluations) — the daemon enforces
// this same budget server-side (a 502 from /submit means it's exhausted), this is just a local
// backstop so a client-side bug can't spin the loop forever.
const MAX_ATTEMPTS = 3;

const executionId = process.argv[2];
if (!executionId) process.exit(0);

const log = (message) => appendLog(logPath(`auto-evaluate-${executionId}`), message);

function runClaude(prompt) {
  return new Promise((resolve, reject) => {
    let child;
    try {
      child = spawn("claude", ["-p", "--output-format", "text"], { stdio: ["pipe", "pipe", "pipe"] });
    } catch (err) {
      reject(err);
      return;
    }

    let stdout = "";
    let stderr = "";
    child.stdout.on("data", (chunk) => { stdout += chunk; });
    child.stderr.on("data", (chunk) => { stderr += chunk; });
    child.on("error", reject);
    child.on("close", (code) => {
      if (code === 0) resolve(stdout);
      else reject(new Error(`claude exited with code ${code}: ${stderr.trim()}`));
    });
    child.stdin.write(prompt);
    child.stdin.end();
  });
}

async function main() {
  const prepareResponse = await fetchWithTimeout(
    `${daemonUrl()}/executions/${executionId}/ai-evaluations/prepare`,
    { method: "POST" },
    5000
  );
  if (!prepareResponse.ok) {
    log(`prepare failed with HTTP status ${prepareResponse.status}`);
    return;
  }

  let { correlationId, prompt } = await prepareResponse.json();

  for (let attempt = 1; attempt <= MAX_ATTEMPTS; attempt++) {
    let response;
    try {
      response = await runClaude(prompt);
    } catch (err) {
      log(`claude CLI invocation failed on attempt ${attempt}: ${err.message} — degrading to no automatic evaluation`);
      return;
    }

    const submitResponse = await fetchWithTimeout(
      `${daemonUrl()}/executions/${executionId}/ai-evaluations/submit`,
      {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ correlationId, response })
      },
      15000
    );

    if (submitResponse.status === 502) {
      log("judge exhausted its retry budget without a valid response");
      return;
    }
    if (!submitResponse.ok) {
      log(`submit failed with HTTP status ${submitResponse.status}`);
      return;
    }

    const result = await submitResponse.json();
    if (result.status === "recorded") {
      log(`recorded AIEvaluation ${result.evaluation.id}`);
      return;
    }

    correlationId = result.correlationId;
    prompt = result.prompt;
  }

  log(`gave up after ${MAX_ATTEMPTS} attempts without a recorded evaluation`);
}

main().catch((err) => log(`unexpected error: ${err.message}`));
