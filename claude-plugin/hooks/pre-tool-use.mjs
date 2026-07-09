#!/usr/bin/env node
// PreToolUse: records a start timestamp per tool_use_id so PostToolUse can compute a real duration.
// Purely local file I/O — no network call, so this stays fast on every tool call.
import { readStdinJson, toolTimerPath, writeJson } from "./lib/state.mjs";

const input = await readStdinJson();
const { session_id: sessionId, tool_use_id: toolUseId, tool_name: toolName } = input;

if (sessionId && toolUseId) {
  writeJson(toolTimerPath(sessionId, toolUseId), { toolName, startedAtMs: Date.now() });
}

process.exit(0);
