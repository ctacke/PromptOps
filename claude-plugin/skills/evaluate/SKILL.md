---
name: evaluate
description: Use when the user types "/promptops evaluate" or asks for an AI judge evaluation of the current (or a recent) execution, or to turn automatic AI evaluation on/off.
---

# /promptops evaluate

Runs (or configures) the AI-judge evaluation pass. Preferred path is **client-delegated evaluation**
(ADR-0010/Phase 12) via the daemon's `prepare_ai_evaluation`/`submit_ai_evaluation_result` MCP
tools — call them directly, don't shell out to curl for this. This replaces MCP's deprecated
`sampling/createMessage` capability: instead of the daemon calling a model itself, it hands you the
judge prompt to answer with your own current reasoning, no separate AI backend involved.

## Running an evaluation (client-delegated — preferred)

1. Find the execution id the same way `/promptops rate` does: the current session's execution id was recorded by the `SessionStart` hook at `${CLAUDE_PLUGIN_DATA}/state/<session_id>.json` (field `executionId`). Glob `${CLAUDE_PLUGIN_DATA}/state/*.json`, pick the most recently modified one, and read its `executionId` field. If no state file exists, tell the user there's nothing to evaluate yet and stop — don't fabricate an execution id.
2. Call `prepare_ai_evaluation` with that execution id. It returns `{ correlationId, prompt }` — it does **not** call any model itself.
3. Answer `prompt` yourself, using your own current reasoning — don't call any other tool or backend to answer it. Review this session's actual task (the acceptance criteria/ADRs/output already embedded in the returned prompt) honestly, as if reviewing someone else's work — don't rubber-stamp your own output just because you wrote it. Produce a JSON object matching the schema the prompt specifies (`satisfiesAcceptanceCriteria`, `adrViolations`, `ignoredRequirements`, `unnecessaryComplexityNotes`, `suggestedPromptImprovements`). Use `null` for `satisfiesAcceptanceCriteria` only if no acceptance criteria were ever given; empty arrays/`null` are fine for the rest if there's nothing to report — don't invent violations or improvements that aren't real.
4. Call `submit_ai_evaluation_result` with the `correlationId` from step 2 and your JSON answer (as a string) from step 3.
   - If it returns `{ status: "recorded", evaluation }` — done, go to step 5.
   - If it returns `{ status: "retry_needed", correlationId, prompt }` — your answer didn't match the required schema. Re-answer the new `prompt` (it includes your previous invalid answer and the specific parse error) and call `submit_ai_evaluation_result` again with the **same or updated** `correlationId` it returned. This can happen up to 3 times total before the daemon gives up (`AIJudgeResponseInvalidException`, surfaced as a tool error) — if that happens, tell the user the judge pipeline itself failed to accept a well-formed answer after 3 tries, don't keep retrying silently forever.
5. Present the result plainly: whether it satisfies acceptance criteria, any ADR violations, ignored requirements, unnecessary complexity notes, and suggested prompt improvements. Don't editorialize about the verdict — this is a second opinion, not a final word.

Calling this again for the same execution is fine — it's additive, not a replace, so it produces another evaluation rather than erroring.

## Fallback: `run_ai_evaluation` (daemon-owned judge)

Use this instead only when there's no live delegating client to answer a prompt — e.g. driving evaluation from a plain script, or a daemon configured with a real backend (`AIExecution:Provider=claude-cli` or similar) that's meant to judge autonomously. Not needed for normal interactive use of this skill.

Call `run_ai_evaluation` with the execution id and (if the configured `IAIExecutionProvider` is the `manual` test stub) `parameters: { "output": "<a JSON string matching the schema above>" }` — the stub echoes that back rather than calling a model, so *you'd* still need to construct the judge JSON yourself and pass it through, same as step 3 above. Omitting `parameters` against the `manual` stub fails after 3 retries with `AIJudgeResponseInvalidException` ("No JSON object found in the response") since the stub has nothing to echo. Against a real daemon-owned provider, omit `parameters` and it judges on its own.

## Turning automatic evaluation on or off

If the user asks to make this happen automatically instead of running it by hand each time (e.g. "turn on automatic AI evaluation," "evaluate every execution automatically"), call `update_ai_evaluation_policy` with `autoEvaluateOnFinish` set accordingly. Tell the user plainly what this does: once on, the daemon runs the AI judge itself whenever an execution finishes, using its configured daemon-owned `IAIExecutionProvider` — **not** client delegation, since there's no live client attached to a background trigger to delegate to. This is off by default because each evaluation is a real judge-model call; mention that when turning it on for the first time, don't just silently flip it.

If the user asks what the current setting is, call `get_ai_evaluation_policy` and report `autoEvaluateOnFinish` plainly.
