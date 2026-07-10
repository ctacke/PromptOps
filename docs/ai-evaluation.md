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

- **Extraction tolerates format drift.** `JsonExtraction.ExtractJsonValue` (a shared utility as of Phase 9, when `AIActivityClassifier` needed the same tolerance for JSON arrays) finds the first balanced `{...}`/`[...]` substring in the response (respecting quoted strings, so a brace inside a string value doesn't throw off the scan) rather than requiring an exact match — a judge that wraps its answer in markdown fences or adds a sentence of prose before/after still parses correctly.
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

## Running it without a manual `POST` — `AIEvaluationPolicy` + `AutoAIEvaluationTrigger`

The line above ("no MCP tool or Claude Code skill this phase") held until a later gap-closing pass: nothing made AI evaluation happen except an explicit `POST`, and nothing wrapped it for the agent either. Both are closed now, in two complementary ways:

**Automatic, opt-in.** `AIEvaluationPolicy` (`src/PromptOps.Domain/Evaluations/AIEvaluationPolicy.cs`) is a single global settings singleton — same shape as `PromotionPolicy` (`docs/promotion-policy.md`) but simpler: one flag, `AutoEvaluateOnFinish`, off by default. When on, `AutoAIEvaluationTrigger` (`src/PromptOps.Infrastructure/Evaluations/`) — a second `IDomainEventHandler<ExecutionRecorded>` registered alongside `ScoreRecomputeTrigger` — runs the judge automatically the moment an execution finishes (`ExecutionRecorded` fires exactly once, from `ExecutionRecord.Finish()`, at the point output/diff data first exists). The actual judge call runs in a detached background task with its own DI scope: `DomainEventPublisher` awaits every handler for an event sequentially, so this must return immediately rather than block the `/executions/{id}/finish` response on a multi-attempt LLM call. A failed background evaluation is logged, never propagated — same discipline as `DebouncedScoreRecomputeScheduler`.

Off by default deliberately: unlike scoring (cheap, local computation), each judge call is a real LLM round trip with retries — auto-firing it unconditionally on every execution isn't something to default to silently.

```
GET  /ai-evaluation-policy   → current policy (lazily bootstraps default: AutoEvaluateOnFinish = false)
PUT  /ai-evaluation-policy   { "autoEvaluateOnFinish": true }
```

**Manual, without curl.** `run_ai_evaluation` (MCP tool, `src/PromptOps.Host/Mcp/AIEvaluationTools.cs`) wraps the same `AIEvaluationService.EvaluateAsync` the REST endpoint calls — reachable whether or not the automatic trigger is on, and safe to call again to force a re-run (additive, not a replace). `get_ai_evaluation_policy`/`update_ai_evaluation_policy` mirror the REST pair for toggling automation without curl. `/promptops evaluate` (`claude-plugin/skills/evaluate/SKILL.md`) wraps all three for the developer: finds the current session's execution id the same way `/promptops rate` does, runs the evaluation, and presents the verdict — or updates the policy if asked to turn automation on/off.

### Testing (automatic trigger)

- `AIEvaluationPolicyTests` (Domain) — default off, `Update` sets the flag.
- `AutoAIEvaluationTriggerTests` (Infrastructure, a real DI container of fakes rather than hand-rolled ones — needed since the trigger resolves `AIEvaluationService` itself via `IServiceScopeFactory`) — toggle off → never called; toggle on → called with the event's `ExecutionId`; `HandleAsync` returns before the gated background call completes (proves it doesn't block); a thrown exception inside the background task never propagates.
- `AutoAIEvaluationEndToEndTests` (Host) — the real proof: with the policy on, finishing an execution over real HTTP produces a persisted `AIEvaluation` with no explicit `POST .../ai-evaluations` call anywhere in the test. Overrides `IAIExecutionProvider` for just this factory, since `ManualAIExecutionProvider` only ever echoes caller-supplied parameters and the automatic trigger has none to supply — a stub that always returns valid judge JSON is what actually exercises the wiring rather than reliably failing against the reference provider's parameter-driven design.

## Testing

- `AIEvaluationTests` (Domain) — construction validation, event raised only on `Record` (not `Rehydrate`), `satisfiesAcceptanceCriteria` allowed to be null.
- `AIJudgeEvaluationProviderTests` (Infrastructure, pure unit tests against a queued stub `IAIExecutionProvider` — no SQLite involved) — happy path, markdown-fence tolerance, missing-optional-fields tolerance, retry-then-succeed, retry-exhaustion-throws, execution-not-found-short-circuits-without-calling-the-judge.
- `AIEvaluationIntegrationTests` (Infrastructure) — round-trips through real SQLite, including list-valued fields.
- `AIEvaluationEndpointsTests` (Host) — full HTTP round trip against the real production DI graph (the real `AIJudgeEvaluationProvider` + `ManualAIExecutionProvider`, driven via `parameters.output`): run → retrieve, 404, 502 on a judge that never returns valid JSON.
