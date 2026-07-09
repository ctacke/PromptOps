# AI Evaluation Pipeline (Phase 7)

`AIEvaluation` is an AI judge's structured assessment of one execution — the automated counterpart to `HumanEvaluation` (docs/human-evaluation.md). Where a human rates subjectively on a 1-5 scale, a judge answers concrete questions: did the output satisfy its acceptance criteria, did it violate a referenced ADR, did it ignore a requirement, did it introduce unnecessary complexity, and how could the prompt that produced it be improved.

## Domain model

```
AIEvaluation (independent aggregate, 0..n per ExecutionRecord, stored separately from HumanEvaluation)
 ├─ id, executionId, judgeProviderId, judgeModel, timestamp
 ├─ satisfiesAcceptanceCriteria   (bool?  — null means the judge had no opinion, e.g. no AC given)
 ├─ adrViolations[]
 ├─ ignoredRequirements[]
 ├─ unnecessaryComplexityNotes    (string?)
 ├─ suggestedPromptImprovements[]
 └─ rawResponse                   (the judge's actual response text, kept for auditability)
```

Same shape of independence as `EngineeringMetrics`/`HumanEvaluation`: `ExecutionId` is a plain value, not a foreign key; every row is immutable and additive — more than one judge run against the same execution produces more than one row, not an upsert.

## `IAIEvaluationProvider`

```csharp
public interface IAIEvaluationProvider
{
    string Name { get; }
    Task<AIEvaluation> EvaluateAsync(Guid executionId, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken = default);
}
```

Built on `IAIExecutionProvider` (ADR-0003) — a judge is just another prompt execution, evaluated with a judge-specific prompt instead of a task prompt. `parameters` mirrors `IMetricCollector`'s design (Phase 5): a real judge implementation mostly ignores it, driving everything from the prompt it builds itself, but the only concrete `IAIExecutionProvider` today (`ManualAIExecutionProvider`, Phase 3) has no model to reason with — it just echoes back `parameters["output"]`. That's how tests and manual/API invocation drive canned judge responses through the exact code path a real judge call would take, without needing a live LLM connection in this environment.

## `AIJudgeEvaluationProvider` (the reference implementation)

1. Loads the `ExecutionRecord` (throws `ExecutionNotFoundException` if it doesn't exist — the provider needs the record's AC/ADR references and output to build a prompt, so this check naturally lives here rather than being duplicated in `AIEvaluationService`, unlike the collector/human-evaluation services in Phases 5-6 where the check has to live at the orchestration layer because not every collector needs the execution).
2. Builds a judge prompt: repository, acceptance criteria, referenced ADRs, and the execution's output, followed by an explicit JSON schema the judge must respond with.
3. Calls `IAIExecutionProvider.ExecuteAsync` and parses the response against that schema.

### Resilience: schema validation + retry, not brittle string matching

This was an explicit design requirement for the phase, not an implementation detail:

- **Extraction tolerates format drift.** `ExtractJsonObject` finds the first balanced `{...}` substring in the response (respecting quoted strings, so a brace inside a string value doesn't throw off the scan) rather than requiring an exact match — a judge that wraps its answer in markdown fences or adds a sentence of prose before/after still parses correctly.
- **Missing optional fields don't fail the parse.** `adrViolations`, `ignoredRequirements`, and `suggestedPromptImprovements` default to empty lists if the judge omits them; only `satisfiesAcceptanceCriteria` being present-but-unparseable, or the JSON itself being malformed, counts as a parse failure.
- **A parse failure triggers a retry, not an immediate error.** Up to 3 attempts (`AIJudgeEvaluationProvider.MaxAttempts`). Each retry appends the invalid response and the specific parse error back into the prompt as a correction, asking the judge to try again — the same self-correction pattern a human reviewer would use, not silent truncation or best-effort partial parsing.
- **Exhausting all attempts is a real failure**, surfaced as `AIJudgeResponseInvalidException` — not silently returning an empty/default `AIEvaluation`. The ingestion API maps this to `502 Bad Gateway`: the judge is the thing that failed, not the caller's request.

`docs/plugin-authoring.md`-style "worked example" note: none of this is plugin-specific — `AIJudgeEvaluationProvider` lives in `PromptOps.Infrastructure` as the daemon's built-in default, the same way `ManualAIExecutionProvider` and `EnvironmentSecretProvider` do. A future `IAIExecutionProvider` for a real backend (Claude Code, ChatGPT, ...) plugs in underneath it without this provider changing at all.

## Ingestion API

```
POST /executions/{id}/ai-evaluations
{ "parameters": { "output": "<canned judge response, or omitted for a real backend>" } }
→ 200 OK { "id": "...", "satisfiesAcceptanceCriteria": true, "adrViolations": [...], ... }
→ 404 if the execution doesn't exist
→ 502 if the judge exhausts its retry budget without returning a valid response

GET /executions/{id}/ai-evaluations
→ 200 OK [ { ... }, { ... } ]   (chronological, may be empty)
```

No MCP tool or Claude Code skill this phase — unlike Phase 6's `/promptops rate`, nothing in the phase's acceptance criteria or ADR-0006 calls for the *agent* to trigger or read AI evaluations mid-session; this is backend judge machinery that later phases (8: scoring, 9: recommendation) consume, not a developer-facing action.

## Testing

- `AIEvaluationTests` (Domain) — construction validation, event raised only on `Record` (not `Rehydrate`), `satisfiesAcceptanceCriteria` allowed to be null.
- `AIJudgeEvaluationProviderTests` (Infrastructure, pure unit tests against a queued stub `IAIExecutionProvider` — no SQLite involved) — happy path, markdown-fence tolerance, missing-optional-fields tolerance, retry-then-succeed, retry-exhaustion-throws, execution-not-found-short-circuits-without-calling-the-judge.
- `AIEvaluationIntegrationTests` (Infrastructure) — round-trips through real SQLite, including list-valued fields.
- `AIEvaluationEndpointsTests` (Host) — full HTTP round trip against the real production DI graph (the real `AIJudgeEvaluationProvider` + `ManualAIExecutionProvider`, driven via `parameters.output`): run → retrieve, 404, 502 on a judge that never returns valid JSON.
