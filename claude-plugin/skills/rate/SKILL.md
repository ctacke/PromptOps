---
name: rate
description: Use when the user types "/promptops rate" or asks to rate, score, or evaluate the current (or a recent) PromptOps execution.
---

# /promptops rate

Submits a `HumanEvaluation` for a PromptOps execution via the daemon's `submit_human_evaluation` MCP tool (registered by this plugin's `.mcp.json` — look for a tool whose name contains both `promptops` and `submit_human_evaluation` among your available tools; call it directly, don't shell out to curl for this).

## 1. Find the execution id

The current session's execution id was recorded by the `SessionStart` hook at `${CLAUDE_PLUGIN_DATA}/state/<session_id>.json` (field `executionId`). You don't know the session id directly, but there's normally only one active session's state file that's fresh — glob `${CLAUDE_PLUGIN_DATA}/state/*.json`, pick the most recently modified one, and read its `executionId` field.

If no state file exists (the daemon was down at session start, or this plugin isn't tracking this session), tell the user there's nothing to rate yet and stop — don't fabricate an execution id.

## 2. Gather the ratings from the user

Ask for each of these (don't assume defaults — a rating that wasn't actually given shouldn't be submitted as if it were):

- **Correctness** (1-5): did the output correctly solve the task?
- **Helpfulness** (1-5): how helpful was the output overall?
- **Architecture** (1-5): architectural quality of the output.
- **Readability** (1-5): readability of the output.
- **Completeness** (1-5): how complete was it relative to what was asked?
- **Hallucinations** (yes/no): did the output contain hallucinated facts, APIs, or behavior?
- **Confidence** (1-5): the user's own confidence in the rating they're giving.
- **Overall satisfaction** (1-5): overall satisfaction with the output.
- **Notes** (optional free text).

Use `AskUserQuestion` for the 1-5 and yes/no fields rather than open-ended prompts, so answers are unambiguous and quick to give.

## 3. Determine the evaluator id

Use the developer's git email (`git config user.email` in the current repo) as `evaluatorId`. Fall back to asking the user if git isn't configured.

## 4. Submit

Call `submit_human_evaluation` with the execution id, evaluator id, and the gathered ratings. Report back the confirmation (evaluation id) plainly — don't editorialize about the scores given.

## Retrieving past ratings

If the user asks to see past ratings for an execution instead of submitting new ones, call `get_human_evaluations` with the execution id and present what comes back — don't fabricate history that isn't there.
