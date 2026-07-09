# Human Evaluation (Phase 6)

`HumanEvaluation` is a developer's structured, subjective rating of one execution — the counterpart to `EngineeringMetrics` (docs/metrics.md), which is objective/automated. Later phases (7: AI evaluation, 8: scoring) weigh both together; neither alone is "the score."

## Domain model

```
HumanEvaluation (independent aggregate, 0..n per ExecutionRecord)
 ├─ id, executionId, evaluatorId, timestamp
 ├─ correctness, helpfulness, architecture, readability, completeness   (1-5)
 ├─ hallucinations                                                       (bool)
 ├─ confidence, overallSatisfaction                                      (1-5)
 └─ notes                                                                (optional free text)
```

Like `EngineeringMetrics.ExecutionId`, `HumanEvaluation.ExecutionId` is a plain value, not a foreign key — submitting a rating never requires the execution to be loaded in the same transaction. Every row is immutable and additive: more than one evaluator (or the same evaluator twice) can rate the same execution, and each submission is its own row, not an upsert. `GET /executions/{id}/evaluations` returns the full history. Reconciling multiple ratings into one number is Phase 8's job (`IScoringProvider`), not this layer's — same split as `EngineeringMetrics`.

Rating fields are validated to the 1-5 range at submission (`HumanEvaluation.Submit` throws `ArgumentOutOfRangeException` outside it); `hallucinations` is a plain yes/no rather than a 1-5 scale since "were there hallucinations" isn't a quality gradient.

## Two ways to reach it, one service underneath

Both the ingestion API and the MCP tools call the same `HumanEvaluationService` — nothing about validation or persistence differs by surface.

### Ingestion API

```
POST /executions/{id}/evaluations
{
  "evaluatorId": "alice@example.com",
  "correctness": 5, "helpfulness": 4, "architecture": 3, "readability": 5, "completeness": 4,
  "hallucinations": false,
  "confidence": 5, "overallSatisfaction": 4,
  "notes": "solid, minor naming nits"
}
→ 200 OK { "id": "...", "executionId": "...", ... }
→ 404 if the execution doesn't exist
→ 400 if a rating is outside 1-5

GET /executions/{id}/evaluations
→ 200 OK [ { ... }, { ... } ]   (chronological, may be empty)
```

### MCP tools

- `submit_human_evaluation(executionId, evaluatorId, correctness, helpfulness, architecture, readability, completeness, hallucinations, confidence, overallSatisfaction, notes?)`
- `get_human_evaluations(executionId)`

ADR-0006 names "submit a rating" as a canonical example of why MCP tools exist alongside the ingestion API (the agent can act on structured data mid-session without shelling out), so submission is exposed over MCP too, not just retrieval — the phase's acceptance bar only requires retrieval-via-MCP, but since both routes hit the same `HumanEvaluationService`, there was no reason to leave submission REST-only. `HumanEvaluationTools` (`src/PromptOps.Host/Mcp/`) is an **instance** tool class (unlike `DaemonTools`, which is static) — the MCP server resolves it per call via DI so `HumanEvaluationService` can be constructor-injected like anywhere else.

## `/promptops rate`

The Claude Code plugin skill (`claude-plugin/skills/rate/SKILL.md`) is what a developer actually types. It:

1. Finds the current session's execution id from `${CLAUDE_PLUGIN_DATA}/state/<session_id>.json` (written by the `SessionStart` hook, Phase 4b) — refuses to fabricate one if no state file exists.
2. Asks the user for each rating via `AskUserQuestion` (not open-ended text, so answers are unambiguous).
3. Derives `evaluatorId` from `git config user.email` in the current repo.
4. Calls `submit_human_evaluation` directly — no shelling out, since the tool is already registered by this plugin's `.mcp.json`.

`/promptops history`'s "not yet implemented" stub (Phase 4b) still stands for now — a general browsable view across executions is Phase 8/9 territory, once scoring exists to make such a view useful. `get_human_evaluations` already lets an agent look up one execution's ratings today, which is enough for `/promptops rate` itself to show "here's what's already been submitted for this execution" if asked.

## Testing

- `HumanEvaluationTests` (Domain) — rating range validation, event raised only on `Submit` (not `Rehydrate`).
- `HumanEvaluationIntegrationTests` (Infrastructure) — round-trips through real SQLite, multiple evaluators on one execution.
- `EvaluationEndpointsTests` (Host) — full HTTP round trip against the real production DI graph: submit → retrieve, 404 on an unknown execution, 400 on an out-of-range rating.
