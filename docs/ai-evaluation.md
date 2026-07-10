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

## Client-Delegated AI Evaluation (ADR-0010) — design

`run_ai_evaluation` requires a real `IAIExecutionProvider` willing to answer the judge prompt. Today's options are both daemon-owned: `ManualAIExecutionProvider` (test stub, echoes `parameters.output`) or a real backend like `ClaudeCliAIExecutionProvider` (shells out to a locally-installed, locally-authenticated `claude` CLI — see below). Both work, but a daemon-owned backend means the daemon needs its own credentials/CLI/API key even when the thing calling it (Claude Code, or any other MCP client) is *already* running an authenticated model conversation right now.

MCP originally solved exactly this with `sampling/createMessage` — a server asks the connected client to run a completion using the client's own model/session. It's gone (deprecated per SEP-2577; never implemented client-side by Claude Code or other mainstream clients — see ADR-0010). Client-delegated evaluation gets the same effect using two ordinary MCP tool calls instead of a protocol feature, so it doesn't depend on Claude Code specifically or on sampling ever coming back.

### Flow

```
Client (Claude Code, or any MCP client)          Daemon
──────────────────────────────────────           ──────
/promptops evaluate
  │
  ├─ prepare_ai_evaluation(executionId) ────────▶ builds judge prompt (JudgePromptBuilder,
  │                                               shared with AIJudgeEvaluationProvider),
  │                                               stores a pending correlation record (TTL),
  │◀───────────── { correlationId, prompt } ───── returns prompt — does NOT call any model
  │
  ├─ (the client itself reasons over `prompt`
  │   using its own current model/session —
  │   no tool call, no new credentials)
  │
  ├─ submit_ai_evaluation_result(                
  │     correlationId, response) ───────────────▶ JudgeResponseParser.TryParse(response)
  │                                                 success → persist AIEvaluation
  │                                                          (judgeProviderId: "client-delegated"),
  │                                                          same repository/domain-event path
  │                                                          as run_ai_evaluation
  │                                                 failure + attempts left → append correction,
  │                                                          keep correlation pending
  │                                                 failure + attempts exhausted → throw
  │                                                          AIJudgeResponseInvalidException (502,
  │                                                          same as run_ai_evaluation today)
  │◀── { status: "recorded", evaluation } ───────
  │      — or —
  │◀── { status: "retry_needed", correlationId,
  │       prompt: <correction> } ─────────────────
  │  (loop: reason again, submit again — up to
  │   MaxAttempts, same limit AIJudgeEvaluationProvider uses today)
```

### New pieces

- **`JudgePromptBuilder`/`JudgeResponseParser`** (`PromptOps.Application.Evaluations`, plain dependency-free static helpers — no I/O, so no interface/DI needed) — `AIJudgeEvaluationProvider.BuildJudgePrompt`/`AppendCorrection`/`TryParseJudgeResponse` extracted here so both the existing autonomous path and the new delegated path build/parse against one identical implementation. `AIJudgeEvaluationProvider` becomes a thin caller of these plus `IAIExecutionProvider`.
- **`IPendingDelegatedEvaluationStore`** (`PromptOps.Application.Evaluations`) — tracks a pending prompt by `correlationId`: `executionId`, current prompt text (grows with each correction), attempt count, expiry. Default `Infrastructure` implementation is an in-memory `ConcurrentDictionary` with TTL-based eviction (e.g. 10 minutes) — **deliberately not persisted**: a delegated evaluation is meant to complete within one live conversation turn, so surviving a daemon restart mid-flight isn't a requirement, and skipping a migration/table for something this short-lived keeps the change small. `correlationId` is server-generated and single-use (removed on success or attempt-exhaustion) purely for request/response hygiene across concurrent evaluations — not a defense against a malicious actor (ADR-0007's single-user/no-RBAC posture is unchanged).
- **`DelegatedAIEvaluationService`** (`PromptOps.Application.Evaluations`) — `PrepareAsync(executionId)` → `{correlationId, prompt}`; `SubmitAsync(correlationId, response)` → recorded `AIEvaluation`, or a retry prompt, reusing `AIEvaluation.Record(...)` + `IAIEvaluationRepository` + `IDomainEventPublisher` exactly like `AIEvaluationService.EvaluateAsync` does.
- **MCP tools** (`PromptOps.Host.Mcp`, alongside `AIEvaluationTools`): `prepare_ai_evaluation(executionId)`, `submit_ai_evaluation_result(correlationId, response)`. `run_ai_evaluation`/`get_ai_evaluation_policy`/`update_ai_evaluation_policy` are unchanged.
- **`/promptops evaluate` (`claude-plugin/skills/evaluate/SKILL.md`)** updated to prefer this flow for interactive use: call `prepare_ai_evaluation`, answer the returned prompt itself (no tool call — the assistant just reasons over it like any other request), call `submit_ai_evaluation_result`, loop on `retry_needed`. Falling back to `run_ai_evaluation` remains documented for daemons with no delegation-capable client attached (e.g. driving evaluation from a plain script).

### What doesn't change

`AutoAIEvaluationTrigger` (automatic evaluation on execution finish, docs/ai-evaluation.md above) runs in a detached background task with no live client attached — there is nothing to delegate *to* at that point. As of Phase 12, automatic/unattended evaluation keeps using a daemon-owned `IAIExecutionProvider` (`AIExecution:Provider` config, ADR-0003): `manual` for tests, `claude-cli` (or a future direct-API provider) for real unattended runs. Client delegation is additive for the interactive case, not a replacement for the autonomous one. **Phase 13 (below) adds a third option** — delegating automatic evaluation from the client-side `SessionEnd` hook instead of the daemon — for teams that want unattended evaluation without giving the daemon its own AI credentials at all.

### Testing (implemented)

All in `PromptOps.Infrastructure.Tests` (no dedicated `PromptOps.Application.Tests` project exists — Application-layer pure logic is tested here, matching where `AIJudgeEvaluationProviderTests` already lived):

- `JudgePromptBuilderTests`/`JudgeResponseParserTests` — prompt content (repository/output/AC/ADRs, "(none given)" fallback), correction-prompt assembly, happy path, markdown-fence tolerance, missing-optional-fields tolerance, no-JSON-found failure.
- `DelegatedAIEvaluationServiceTests` (hand-rolled fakes matching this repo's existing style — `FakeExecutionRepository`, `FakeAIEvaluationRepository`, `FakeDomainEventPublisher`) — prepare returns a correlation id + prompt and throws `ExecutionNotFoundException` for an unknown execution; submit with valid JSON persists (`judgeProviderId: "client-delegated"`), publishes domain events, and removes the pending entry; submit with invalid JSON returns a retry prompt and keeps the correlation id usable for a second attempt; exhausting `JudgePromptBuilder.MaxAttempts` throws `AIJudgeResponseInvalidException`; submitting an unknown correlation id throws `PendingEvaluationNotFoundException` instead of silently no-oping.
- `InMemoryPendingDelegatedEvaluationStoreTests` — create/get round-trip, unknown-id lookup, TTL eviction (via a manual `TimeProvider`, permanent once evicted), update refreshes prompt/attempt/expiry, remove.

Not yet added: a Host-level MCP end-to-end test for `prepare_ai_evaluation`/`submit_ai_evaluation_result` — no existing test exercises an MCP tool class directly today (`AIEvaluationTools` itself isn't tested either; only the services/endpoints underneath it are), so `DelegatedAIEvaluationTools`' thin pass-through follows that same convention rather than introducing a new one.

## Client-Side Automatic Evaluation (ADR-0010 amendment, Phase 13)

`AutoAIEvaluationTrigger` needs a daemon-owned `IAIExecutionProvider` because it fires from a detached background task (`ExecutionRecorded`, raised once from `ExecutionRecord.Finish()`) with no live MCP client attached to delegate to via `prepare_ai_evaluation`/`submit_ai_evaluation_result`. Getting automatic evaluation *without* the daemon owning credentials means moving the delegation to a place that both (a) reliably runs once per session and (b) has access to an already-authenticated AI client. The per-repo plugin's `SessionEnd` hook (`claude-plugin/hooks/session-end.mjs`) is exactly that place: it's a plain Node.js script running natively on the developer's machine — not inside the daemon's container — where `claude` (the CLI) is already installed and already logged in, for the sole reason that Claude Code itself needs it to be. It's also already the hook that calls `POST /executions/{id}/finish` (see `session-end.mjs`'s own comment on why `SessionEnd`, not `Stop`, is used — `Stop` fires once per conversational turn, `SessionEnd` fires once per session, matching `ExecutionRecord`'s single `InProgress → Finished` transition).

### Why not reuse the MCP tools directly

`prepare_ai_evaluation`/`submit_ai_evaluation_result` (Phase 12) are MCP tools — hooks are not MCP clients, they're scripts that `fetch()` the daemon's loopback ingestion API (ADR-0006). Reaching `DelegatedAIEvaluationService` from a hook needs two new **ingestion HTTP endpoints** that are thin wrappers around the exact same service, mirroring the existing `POST/GET /executions/{id}/ai-evaluations` pair:

```
POST /executions/{id}/ai-evaluations/prepare
  → 200 OK { "correlationId": "...", "prompt": "..." }
  → 404 if the execution doesn't exist

POST /executions/{id}/ai-evaluations/submit
  { "correlationId": "...", "response": "..." }
  → 200 OK { "status": "recorded", "evaluation": { ... } } | { "status": "retry_needed", "correlationId": "...", "prompt": "..." }
  → 404 if the correlation id is unknown/expired
  → 502 if attempts are exhausted without a valid response
```

No change to `DelegatedAIEvaluationService` itself — same as the MCP tools, these endpoints just call `PrepareAsync`/`SubmitAsync`.

### Policy: a mechanism, not just an on/off switch

`AIEvaluationPolicy` today is a single bool (`AutoEvaluateOnFinish`). Phase 13 needs to know not just *whether* automatic evaluation should happen but *which* of two mechanisms should do it — the daemon (`AutoAIEvaluationTrigger`, existing) or the client hook (new) — otherwise turning both on would evaluate every execution twice (harmless since `AIEvaluation` rows are additive, but wasteful, and confusing about which `judgeProviderId` "the" automatic evaluation came from). Add a second field:

```csharp
public enum AutoEvaluationMechanism { Daemon, ClientHook }

public sealed class AIEvaluationPolicy
{
    public bool AutoEvaluateOnFinish { get; private set; }
    public AutoEvaluationMechanism Mechanism { get; private set; } = AutoEvaluationMechanism.Daemon; // back-compat default
    // ...
}
```

`GET/PUT /ai-evaluation-policy` (and `get_ai_evaluation_policy`/`update_ai_evaluation_policy`) gain an optional `mechanism` field, defaulting to `Daemon` when omitted — every existing installation/test that only ever set `autoEvaluateOnFinish` keeps behaving exactly as it does today. `AutoAIEvaluationTrigger` adds one more guard: only fires when `AutoEvaluateOnFinish && Mechanism == Daemon`. When `Mechanism == ClientHook`, the daemon does nothing automatically — the hook is entirely responsible.

### `session-end.mjs` changes

After the existing `finish` call (unchanged), and only when the daemon's policy says `AutoEvaluateOnFinish && Mechanism == ClientHook`:

```
SessionEnd (session-end.mjs)
  │
  ├─ POST .../finish (existing, unchanged, best-effort/4s timeout)
  │
  ├─ GET /ai-evaluation-policy ──▶ { autoEvaluateOnFinish: true, mechanism: "clientHook" }
  │
  ├─ spawn detached child process (auto-evaluate.mjs), stdio ignored, unref'd,
  │  THEN session-end.mjs exits immediately — does not wait for the child
  │
  ┊  (independently, in the detached child:)
  ┊  POST .../ai-evaluations/prepare ──▶ { correlationId, prompt }
  ┊  spawn `claude -p --output-format text`, write `prompt` to stdin, read stdout
  ┊  POST .../ai-evaluations/submit { correlationId, response: <stdout> }
  ┊    "retry_needed" → re-run `claude -p` with the corrected prompt, up to MaxAttempts
  ┊    "recorded" or exhausted-attempts error → log locally, exit
```

The detach is the important part: `session-end.mjs`'s own doc comment already establishes "SessionEnd hooks can't block session termination anyway" for the fast `finish` call — spawning a whole `claude -p` round trip (which can take many seconds) inline would either hang session termination or race Node's process exit. A separate, detached (`{ detached: true, stdio: "ignore" }`, `.unref()`) child process lets `session-end.mjs` return immediately while the evaluation runs independently in the background, matching `AutoAIEvaluationTrigger`'s own existing discipline: "a failed background evaluation is logged, never propagated."

### What this does and doesn't solve

- **Does:** genuine unattended/automatic evaluation with zero AI credentials in the daemon or its container — the daemon never touches a model, same as the interactive delegated path, just triggered by the hook instead of a human typing `/promptops evaluate`.
- **Doesn't:** eliminate the cost/latency of a real judge call on every session end — this is still a real `claude -p` invocation, so it must stay opt-in (`Mechanism == ClientHook` is never the silent default), same reasoning `AutoEvaluateOnFinish` itself already has.
- **Doesn't stay provider-agnostic at the hook layer** — unlike the daemon's `prepare`/`submit` endpoints (which don't know or care what answered them), `session-end.mjs` is Claude-Code-specific by construction (it's *this* plugin's hook). A different MCP client wanting the same automatic behavior would need its own equivalent hook/extension shelling out to *its own* CLI — the daemon-side contract stays identical either way, so this doesn't reintroduce a Claude-specific assumption into the daemon itself, only into this one plugin's implementation of automatic evaluation.

### Testing (implemented)

- `AIEvaluationPolicyTests` (Domain) — extended for the new `Mechanism` field: defaults to `Daemon`, round-trips through `Update`/`Rehydrate`.
- `AutoAIEvaluationTriggerTests` (Infrastructure) — extended: `Mechanism == ClientHook` means the trigger does not fire even when `AutoEvaluateOnFinish` is true.
- `AIEvaluationPolicyEndpointsTests`/`AIEvaluationEndpointsTests` (Host) — extended/added for `mechanism` on `GET/PUT /ai-evaluation-policy`, and 200/404/502 cases for `POST .../ai-evaluations/prepare` and `POST .../ai-evaluations/submit`, mirroring the MCP tool behavior already covered by `DelegatedAIEvaluationServiceTests`.

Not yet added: a scripted hook-level smoke test (installing the plugin in a scratch repo, running a session end-to-end with a real `claude` CLI available, and inspecting what landed in the daemon) — this repo has no existing harness for exercising the `.mjs` hooks themselves (only their HTTP/MCP-facing daemon side is tested), and standing one up needs a real Docker daemon + authenticated `claude` CLI, neither available in an automated test run. `session-end.mjs`/`auto-evaluate.mjs` are covered by the same discipline `session-end.mjs`'s other calls already follow: best-effort, timeout-bounded, detached where a real LLM round trip is involved, and reviewed by hand against the endpoints they call.
