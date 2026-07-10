---
name: evaluate
description: Use when the user types "/promptops evaluate" or asks for an AI judge evaluation of the current (or a recent) execution, or to turn automatic AI evaluation on/off.
---

# /promptops evaluate

Runs (or configures) the AI-judge evaluation pass via the daemon's `run_ai_evaluation`, `get_ai_evaluation_policy`, and `update_ai_evaluation_policy` MCP tools (registered by this plugin's `.mcp.json` — call them directly, don't shell out to curl for this).

## Running an evaluation

1. Find the execution id the same way `/promptops rate` does: the current session's execution id was recorded by the `SessionStart` hook at `${CLAUDE_PLUGIN_DATA}/state/<session_id>.json` (field `executionId`). Glob `${CLAUDE_PLUGIN_DATA}/state/*.json`, pick the most recently modified one, and read its `executionId` field. If no state file exists, tell the user there's nothing to evaluate yet and stop — don't fabricate an execution id.
2. Call `run_ai_evaluation` with that execution id.
3. Present the result plainly: whether it satisfies acceptance criteria, any ADR violations, ignored requirements, unnecessary complexity notes, and suggested prompt improvements. Don't editorialize about the verdict — this is a second opinion, not a final word.

Calling this again for the same execution is fine — it's additive, not a replace, so it produces another evaluation rather than erroring.

## Turning automatic evaluation on or off

If the user asks to make this happen automatically instead of running it by hand each time (e.g. "turn on automatic AI evaluation," "evaluate every execution automatically"), call `update_ai_evaluation_policy` with `autoEvaluateOnFinish` set accordingly. Tell the user plainly what this does: once on, the daemon runs the AI judge itself whenever an execution finishes — no `/promptops evaluate` needed. This is off by default because each evaluation is a real judge-model call; mention that when turning it on for the first time, don't just silently flip it.

If the user asks what the current setting is, call `get_ai_evaluation_policy` and report `autoEvaluateOnFinish` plainly.
