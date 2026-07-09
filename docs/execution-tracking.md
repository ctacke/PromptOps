# Execution Tracking (Phase 3)

Records what happened when a prompt was used: who ran it, in what repo/branch/commit, against what task, what tools it invoked, and what came out of it. This is the second aggregate the daemon persists (`docs/prompt-repository.md` covers the first, `Prompt`).

## The push model — why the daemon never reads git itself

The daemon runs in a container with no filesystem access to any repo (ADR-0005 §9). Every fact that requires reading a repo's working directory — repository name, branch, commit, files changed, lines added/removed — is computed by whatever calls in (a real Claude Code hook in Phase 4b; a fixture payload in this phase's tests) and **pushed** via the endpoints below. The daemon never pulls this data itself. `IContextProvider` (defined in Phase 1) remains the seam for context the daemon genuinely *can* fetch itself over the network — Jira, an ADR store with an API — but nothing implements it yet; that's Phase 11+.

This has a concrete consequence in the domain model: `ExecutionRecord.PromptVersionId` is a plain value, not a foreign key. `ExecutionRecord` is an independent aggregate from `Prompt` — recording an execution never requires the referenced prompt version to be loaded, or even to exist yet in the same transaction. Same rationale as `PromptDependency.TargetPromptVersionId` in Phase 2.

## Domain model

```
ExecutionRecord (aggregate root, extends AggregateRoot for domain events)
 ├─ id, promptVersionId, developerId, timestamp
 ├─ DevelopmentContext: repository, branch, commit, taskId,
 │    referencedDocuments[], referencedADRs[], acceptanceCriteria[], languages[]
 ├─ inputs (dictionary)
 ├─ status: InProgress → Finished
 ├─ output, executionTime, aiProviderId, model, modelParameters   (set at Finish)
 ├─ filesChanged[], linesAdded, linesDeleted                      (set at Finish)
 └─ toolUsage[]: { name, count, duration, recordedAt }             (appended any time before Finish)
```

Lifecycle: `Start` → any number of `RecordToolUsage` → exactly one `Finish`. `Finish` raises `ExecutionRecorded` — deliberately on Finish, not Start, since that's the point a full record (including output) exists to publish something about.

## `DevelopmentContext.Languages`

Decided while discussing this phase, not part of the original Phase 3 scope: the repo's dominant language(s) will be auto-detected by the Phase 4b `SessionStart` hook (file extensions, manifest files like `*.csproj`/`package.json`/`pyproject.toml`) and pushed alongside repository/branch/commit — never inferred or fetched by the daemon itself, consistent with the push model above. The field was added to `DevelopmentContext` now, ahead of the hook that will actually populate it, specifically to avoid a schema retrofit once Phase 4b and later phases (recommendation filtering by language, Phase 9) start depending on the aggregate's shape. It's an empty list until then — nothing populates it yet.

This was a deliberate reversal of the earlier per-language-storage question (see the conversation that shaped ADR-0005 §9): prompts themselves stay un-partitioned by language so cross-language history keeps informing recommendations, but *executions* now carry the language(s) of the repo they ran in, so recommendations can later be filtered/boosted by language without ever siloing the underlying prompt data.

## Domain events, and why there's no MediatR

ADR-0008 named MediatR as the mechanism for domain events. Building this phase, I checked the license on the package before adding it (`~/.nuget/packages/mediatr/14.2.0/LICENSE.md`) and found MediatR moved to a dual license: Reciprocal Public License 1.5 (copyleft — would require this project to release its own source under the same terms) or a paid commercial license. Both are incompatible with "open-source friendly" (an explicit requirement from day one). **ADR-0008 is superseded**: domain events are dispatched by a small hand-rolled publisher instead —

- `Domain` defines `IDomainEvent` (marker, zero dependencies) and `AggregateRoot` (accumulates events, exposes `DomainEvents`, `ClearDomainEvents()`).
- `Application` defines `IDomainEventHandler<TEvent>` and `IDomainEventPublisher`, implemented by `DomainEventPublisher`, which resolves `IDomainEventHandler<TConcrete>` instances via DI (`IServiceProvider.GetServices`) for the event's runtime type and invokes each via reflection.
- `ExecutionService` publishes and clears an aggregate's events immediately after a successful save.

This is intentionally minimal — no pipeline behaviors, no request/response mediation, just publish-to-registered-handlers. If a genuine need for more (e.g. pipeline behaviors for cross-cutting concerns) shows up later, a from-scratch, permissively-licensed replacement is a small addition, not a rewrite; nothing outside `Application`/`Domain` depends on the mechanism.

## Endpoints (the loopback ingestion API, ADR-0006)

All under `/executions`. This is the daemon-side half of the hook contract Phase 4b's Claude Code plugin will call into:

| Endpoint | Called by (production) | Purpose |
|---|---|---|
| `POST /executions/start` | `SessionStart` hook | Opens an execution; the hook supplies repository/branch/commit/task, computed locally |
| `POST /executions/{id}/tool-usage` | `PreToolUse`/`PostToolUse` hooks | Appends one tool-usage record |
| `POST /executions/{id}/finish` | `Stop` hook | Closes the execution with output + diff stats, computed locally; raises `ExecutionRecorded` |
| `GET /executions/{id}` | anything | Reads back the current state |

Unknown execution ids return `404` from `tool-usage`/`finish`/`GET` (via `ExecutionNotFoundException`, caught at the endpoint). No auth yet — not needed until the daemon actually binds beyond loopback (ADR-0007), which isn't until Phase 4a.

## The other execution path: `ExecuteAndRecordAsync`

Separate from the push-based flow above, `ExecutionService.ExecuteAndRecordAsync` calls `IAIExecutionProvider.ExecuteAsync` itself and records the result in one call. This exists because not every execution is hook-reported — Phase 7's AI evaluation pipeline, for instance, is explicitly built on `IAIExecutionProvider` and needs the daemon to drive execution directly. Phase 3 ships `ManualAIExecutionProvider` (echoes back a manually-supplied `output` input) as the reference implementation proving this path works; there is no HTTP endpoint for it yet since nothing needs one until a real consumer does.

## Schema

Two tables, following the same pattern as Phase 2 (separate persistence records, `ValueGeneratedNever()` on all client-generated Guid keys — see `docs/prompt-repository.md` for why that matters):

```
Executions
 ├─ Id (PK), PromptVersionId (plain value, not FK), DeveloperId, Timestamp
 ├─ Repository, Branch, Commit, TaskId
 ├─ ReferencedDocuments / ReferencedADRs / AcceptanceCriteria   (JSON array columns)
 ├─ Inputs                                                       (JSON object column)
 ├─ Status, Output, ExecutionTimeMs, AiProviderId, Model, ModelParameters
 └─ FilesChanged (JSON array), LinesAdded, LinesDeleted

ExecutionToolUsages
 ├─ Id (PK), ExecutionId (FK → Executions, cascade)
 ├─ Name, Count, DurationMs, RecordedAt
```

One SQLite-specific wrinkle worth flagging for future work in this area: SQLite can't `ORDER BY` a `DateTimeOffset` column in SQL. `ExecutionRepository` loads `ToolUsage` unordered and sorts client-side (`List.Sort`) immediately after — cheap for a per-execution list, and `ExecutionMapper.ApplyChanges`'s append-only reconciliation (`Skip(entity.ToolUsage.Count)`) depends on that order staying stable across the lifetime of a tracked entity, which it does since new entries are only ever appended after the sorted point.

## Testing

`tests/PromptOps.Infrastructure.Tests/ExecutionTrackingIntegrationTests.cs` — real SQLite (via the same `SqliteFixture` as Phase 2), covering the full Start → RecordToolUsage → Finish flow from a fixture payload shaped like a real hook's, the `ExecutionRecorded` event firing exactly once (on Finish, not Start) and being observed through a real `IDomainEventPublisher` + registered handler, not-found handling, and `ExecuteAndRecordAsync`.

`tests/PromptOps.Host.Tests/ExecutionEndpointsTests.cs` — the same flow again, but over real HTTP against `WebApplicationFactory`, proving the production DI graph (JSON binding, routing, `ExecutionService` resolution, the domain event publisher) is wired correctly end to end, not just that `ExecutionService` works in isolation.

`tests/PromptOps.Domain.Tests/Executions/ExecutionRecordTests.cs` — pure domain invariants: status transitions, negative line-count rejection, tool usage after finish rejected, event-raising, rehydration never re-raises events.
