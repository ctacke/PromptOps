import { mkdirSync, readFileSync, writeFileSync, rmSync, existsSync } from "node:fs";
import { dirname, join } from "node:path";
import { tmpdir } from "node:os";

// A well-known placeholder: Phase 4b sessions aren't tied to a specific PromptVersion yet
// (prompt selection/recommendation land in Phases 6/9). ExecutionRecord.PromptVersionId is a
// plain value, not a foreign key (see docs/execution-tracking.md), so this is safe to record now
// and backfill with a real selection once the daemon can make one.
export const UNTRACKED_PROMPT_VERSION_ID = "00000000-0000-0000-0000-000000000000";

export function daemonUrl() {
  return process.env.PROMPTOPS_DAEMON_URL || "http://127.0.0.1:5179";
}

function stateDir() {
  const root = process.env.CLAUDE_PLUGIN_DATA || join(tmpdir(), "promptops-plugin");
  return join(root, "state");
}

export function executionStatePath(sessionId) {
  return join(stateDir(), `${sessionId}.json`);
}

export function toolTimerPath(sessionId, toolUseId) {
  return join(stateDir(), "tools", sessionId, `${toolUseId}.json`);
}

export function readJson(path) {
  if (!existsSync(path)) return null;
  try {
    return JSON.parse(readFileSync(path, "utf8"));
  } catch {
    return null;
  }
}

export function writeJson(path, value) {
  mkdirSync(dirname(path), { recursive: true });
  writeFileSync(path, JSON.stringify(value), "utf8");
}

export function removeFile(path) {
  try {
    rmSync(path, { force: true });
  } catch {
    // best-effort cleanup only
  }
}

export async function readStdinJson() {
  const chunks = [];
  for await (const chunk of process.stdin) chunks.push(chunk);
  const raw = Buffer.concat(chunks).toString("utf8");
  return raw ? JSON.parse(raw) : {};
}

export async function fetchWithTimeout(url, options = {}, timeoutMs = 3000) {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), timeoutMs);
  try {
    return await fetch(url, { ...options, signal: controller.signal });
  } finally {
    clearTimeout(timer);
  }
}

/** Prints hookSpecificOutput JSON on stdout, which Claude Code parses for additionalContext etc. */
export function emitHookOutput(hookEventName, fields) {
  process.stdout.write(
    JSON.stringify({ hookSpecificOutput: { hookEventName, ...fields } })
  );
}
