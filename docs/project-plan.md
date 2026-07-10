# PromptOps — Phase 0: Project Plan

Companion to [architecture.md](./architecture.md). Each phase below ships working, reviewable software. Phases are implemented one at a time; a phase is not started until the previous one is approved.

Two artifacts run through every phase from Phase 4 onward: the **daemon** (Docker image, owns storage and does the real work, one instance per developer machine) and the **plugin** (thin, per-repo, Claude Code plugin — hooks + skills, no compiled code). Phases 1–3 build the daemon's internals before either artifact is packaged.

## Phase 1 — Domain Core & Solution Skeleton

**Deliverables**
- .NET 10 solution: `PromptOps.Domain`, `PromptOps.Application`, `PromptOps.Infrastructure`, `PromptOps.Host` (the daemon's composition root and entry point — becomes the Docker image's entrypoint in Phase 4), `plugins/` (empty, with a `PromptOps.Plugin.Sdk` project defining `IPromptOpsPlugin`).
- Core entities/value objects: `Prompt`, `PromptVersion`, `PromptMetadata` with versioning invariants (immutable content, `parentVersionId` lineage).
- Provider interfaces from ADR-0003 defined as empty contracts in `Application` (no implementations yet).
- DI composition root in `Host` that boots with zero plugins loaded.
- xUnit test project + architecture fitness tests (NetArchTest) enforcing ADR-0002 layering.

**Acceptance criteria**
- Solution builds and `Host` starts with no configured plugins.
- Fitness test fails the build if `Domain`/`Application` reference `Infrastructure`, EF Core, or any plugin.
- Unit tests cover: version creation, immutability, lineage linking, invalid-state rejection (e.g. duplicate version numbers).

**Testing:** unit tests only (no external dependencies yet).

**Docs:** `README.md` (solution layout, how to build/test), `docs/plugin-authoring.md` stub.

## Phase 2 — Prompt Repository (versioning + metadata)

**Deliverables**
- `IPromptRepository` + EF Core/SQLite implementation, initial migration.
- Application services: `CreatePrompt`, `CreateVersion`, `TagPrompt`, `DeprecatePromptVersion`, `AddPromptDependency`.
- Metadata stored in a table separate from version content, per requirement.

**Acceptance criteria**
- Can create a prompt, add multiple versions, retrieve full version history and changelog.
- Metadata (tags/owners/categories) queryable independently of content.
- Dependency links between prompt versions are persisted and traversable.

**Testing:** integration tests against an ephemeral SQLite file (no external service required — consistent with the local-first, nothing-to-stand-up goal); unit tests for versioning/dependency rules.

**Docs:** `docs/prompt-repository.md` (schema + usage), ER diagram.

## Phase 3 — Execution Tracking

**Deliverables**
- `ExecutionRecord` entity + repository.
- Daemon-side endpoints (used later by hooks, but built and tested standalone here) that **accept pushed context** rather than reading a repo's filesystem directly: `start execution` (repository, branch, commit — supplied by the caller), `record tool usage`, `finish execution` (files changed, lines added/removed — also supplied by the caller). This is a deliberate consequence of the daemon running in a container with no filesystem access to any repo — git-derived facts are computed locally by whatever calls in (a real Claude Code hook in Phase 4b; fixture payloads in this phase's tests) and pushed, not pulled.
- `IContextProvider` port defined for context sources the daemon *can* reach itself over the network (Jira, ADR/doc stores with an API) — no concrete implementation yet, just the seam.
- `IAIExecutionProvider` interface + a minimal reference implementation that records a manually-supplied output (the real Claude Code integration is hook-driven, built in Phase 4b — this phase proves the recording pipeline, not the integration).

**Acceptance criteria**
- An execution can be recorded end-to-end given a fixture payload shaped like what a real git-aware hook would send (repository/branch/commit/diff stats).
- `ExecutionRecorded` domain event fires and is observable in tests.

**Testing:** integration tests using fixture context payloads; unit tests for the recording use case.

**Docs:** `docs/execution-tracking.md`.

## Phase 4a — Core Daemon Packaging

**Deliverables**
- Multi-stage `Dockerfile` producing a single image containing `Host` + `Infrastructure` + all in-tree plugins.
- `docker-compose.yml` (or an equivalent run script) wiring the image to a named volume for the SQLite database + artifact storage.
- MCP-over-HTTP endpoint (streamable HTTP transport) exposing an initially empty/minimal tool set (health check, version).
- The loopback ingestion API from Phase 3 exposed over HTTP, bound to `localhost` only.

**Acceptance criteria**
- `docker run`/`docker compose up` starts the daemon; it survives a restart with data intact (volume persistence verified).
- The daemon is unreachable from outside the host machine (binds loopback only) and reachable from the host machine itself.
- An MCP client can connect over HTTP and see the health/version tool.

**Testing:** a scripted smoke test — start the container, hit the ingestion API and MCP endpoint, stop/restart, confirm data survived.

**Docs:** `docs/daemon-setup.md` (how to start it, where data lives, how to upgrade the image).

## Phase 4b — Thin Claude Code Plugin

**Deliverables**
- `plugin.json` manifest (name, description, hooks, MCP server registration).
- `hooks/` — shell scripts for `SessionStart` (gather repo/branch/commit via local `git`, call the daemon's "start execution"), `PreToolUse`/`PostToolUse` (stream tool-usage stats), `Stop` (compute diff stats via local `git`, call "finish execution").
- A one-time setup step (skill or install hook) that registers the daemon as a remote MCP server and offers to start the daemon via Docker if it isn't already running.
- `skills/`/slash-command stubs: `/promptops rate`, `/promptops recommend`, `/promptops history` (calling the daemon; can return "not yet implemented" until Phases 6/9 land — the wiring is what this phase proves).

**Acceptance criteria**
- Installing the plugin into a scratch repo (same mechanism as installing `context-mode` in this environment) wires up hooks and the MCP registration with no manual config editing beyond the one-time daemon-start step.
- Starting a session in the scratch repo fires `SessionStart`, which successfully reaches the daemon (visible in the daemon's stored `ExecutionRecord`s).
- Ending the session fires `Stop` and the record is finalized with real diff stats from that repo.

**Testing:** end-to-end test in a real scratch repo with the daemon running locally, following the same style of verification used elsewhere in this environment (install the plugin, run a session, inspect what landed in the daemon).

**Docs:** `docs/installing-promptops.md` (the actual "copy/install into a repo" instructions this whole project was built to produce), `docs/plugin-authoring.md` updated with the hook contract.

## Phase 5 — Engineering Metric Collectors (first real plugins)

**Deliverables**
- `IMetricCollector` interface, plugin manifest loading (ADR-0004) made real (currently stubbed since Phase 1) — these are daemon-side plugins, unrelated to the per-repo Claude Code plugin from Phase 4b.
- `SonarMetricCollector` plugin (SonarQube Web API client).
- `BuildResultCollector` plugin (parses trx/JUnit test results + Cobertura coverage, submitted via the ingestion API since the daemon has no filesystem access to CI artifacts either).
- `EngineeringMetrics` persisted keyed to `ExecutionRecord`, additive across multiple collection events.

**Acceptance criteria**
- Running the Sonar collector against a real project populates `EngineeringMetrics` fields.
- Adding/removing a collector plugin is a config change to the daemon, not a code change.

**Testing:** unit tests with mocked Sonar responses; integration tests parsing sample trx/Cobertura fixtures.

**Docs:** `docs/plugin-authoring.md` (filled in with a worked example), `docs/metrics.md`.

## Phase 6 — Human Evaluation

**Deliverables**
- `HumanEvaluation` entity/repository.
- `/promptops rate` skill (from the Phase 4b stub) fully wired: submit correctness, helpfulness, architecture, readability, completeness, hallucinations, confidence, overall satisfaction, notes.
- Retrieval via MCP tool (so the agent itself can reference past ratings) and ingestion API.

**Acceptance criteria**
- A developer can submit and retrieve a human evaluation for a given execution from within a Claude Code session via `/promptops rate`.

**Testing:** integration tests against the daemon.

**Docs:** `docs/human-evaluation.md`.

## Phase 7 — AI Evaluation Pipeline

**Deliverables**
- `IAIEvaluationProvider` interface + reference implementation built on `IAIExecutionProvider`, asking: AC satisfied? ADR violated? requirements ignored? unnecessary complexity introduced? suggested prompt improvements?
- `AIEvaluation` persisted separately from `HumanEvaluation`.

**Acceptance criteria**
- Given an `ExecutionRecord` with AC/ADR references, the pipeline produces a structured, persisted `AIEvaluation`.
- Judge output parsing is resilient to minor response-format drift (schema validation + retry, not brittle string matching).

**Testing:** unit tests against a stub AI provider with canned/edge-case responses.

**Docs:** `docs/ai-evaluation.md`.

## Phase 8 — Scoring Engine

**Deliverables**
- `ScoringConfig` entity (named, versioned, weighted).
- `IScoringProvider` + default weighted-sum implementation combining human rating, Sonar, tests, build, AC satisfaction, manual fixes, review comments, regression bugs into `PromptScore`.
- Recompute-on-event (debounced) + on-demand recompute.

**Acceptance criteria**
- Changing a `ScoringConfig`'s weights changes computed scores deterministically per a documented formula.
- Scores record which `ScoringConfig` version produced them (reproducibility).

**Testing:** unit tests covering zero-weight and missing-input edge cases.

**Docs:** `docs/scoring.md` with the formula spelled out.

## Phase 9 — Recommendation Engine v1 (tag + historical ranking)

**Deliverables**
- `IRecommendationProvider` interface + tag-search/historical-`PromptScore`-ranking implementation, querying across **all repos in the shared database by default**, filterable to the current repo.
- `IActivityClassifier` interface + `AIActivityClassifier` (built on `IAIExecutionProvider`, same "reuse the provider abstraction" pattern as `IAIEvaluationProvider` — no separate AI dependency): classifies a free-text task description into activity tags before recommendation runs. This is the piece that fills the gap identified when Phase 3 shipped — nothing previously produced the tags `RecommendAsync` needs for a session that's just starting.
- `/promptops recommend` skill fully wired as classify-then-recommend (task description → `IActivityClassifier` → tags → `IRecommendationProvider`), plus an MCP tool so the agent can call it mid-session. The developer supplies a task description, not tags — classification is internal, not a separate user-facing step.

**Acceptance criteria**
- Given tags/category, returns ranked prompt versions with a stated rationale (not a black-box score), drawing on history from any repo on the machine.
- A brand-new repo with zero history of its own still gets useful recommendations if a similar task has been run in another repo.
- Given a free-text task description, `IActivityClassifier` returns tags that plausibly match its activity (e.g. a description containing a stack trace/error message classifies toward debugging-flavored tags) without the developer declaring a category themselves.

**Testing:** integration tests with seeded execution/score history across multiple simulated repos; classifier tests against a stub `IAIExecutionProvider` with canned task descriptions and expected tag categories (same pattern as Phase 7's planned AI evaluation tests).

**Docs:** `docs/recommendations.md`.

## Phase 10 — Semantic Search / Knowledge Base

**Deliverables**
- `IEmbeddingProvider` abstraction, in-process similarity index (e.g. `sqlite-vec` or brute-force cosine — single shared database, single-machine scale, no need for a dedicated vector database).
- `IRecommendationProvider` upgraded to blend semantic + tag + historical ranking, still spanning all repos by default.

**Acceptance criteria**
- Semantically similar past tasks surface even without exact tag overlap, including across repos.

**Testing:** integration tests with fixture embeddings.

**Docs:** `docs/knowledge-base.md`.

## Phase 12 — Client-Delegated AI Evaluation

**Context:** `run_ai_evaluation` (Phase 7) needs a real `IAIExecutionProvider` to answer the judge prompt — today that's either the `manual` test stub or a daemon-owned backend that shells out/calls an API with its own credentials. MCP's `sampling/createMessage` — the protocol feature that would let the daemon ask the *connected client* to run the completion on its own already-authenticated model — was deprecated (SEP-2577) before Claude Code or any mainstream client implemented the client side. This phase gets the same effect (no duplicated credentials, provider-agnostic) via two ordinary MCP tool calls instead. Full design: `docs/ai-evaluation.md` §"Client-Delegated AI Evaluation"; decision record: ADR-0010.

**Deliverables**
- `JudgePromptBuilder`/`JudgeResponseParser` extracted from `AIJudgeEvaluationProvider` into shared, dependency-free helpers (`PromptOps.Application.Evaluations`) — no behavior change to the existing autonomous path, just de-duplication ahead of a second caller.
- `IPendingDelegatedEvaluationStore` port + an in-memory, TTL-based default implementation (deliberately not persisted — see rationale in `docs/ai-evaluation.md`).
- `DelegatedAIEvaluationService` (`PrepareAsync`/`SubmitAsync`), reusing `AIEvaluation.Record`/`IAIEvaluationRepository`/`IDomainEventPublisher` exactly like `AIEvaluationService.EvaluateAsync`.
- Two new MCP tools: `prepare_ai_evaluation(executionId)`, `submit_ai_evaluation_result(correlationId, response)`.
- `/promptops evaluate` (`claude-plugin/skills/evaluate/SKILL.md`) updated to prefer the delegated flow, with `run_ai_evaluation` documented as the fallback for non-interactive/no-client-attached use.

**Acceptance criteria**
- `prepare_ai_evaluation` never calls any model — it only builds and returns a prompt + correlation id.
- A valid `submit_ai_evaluation_result` response persists an `AIEvaluation` (`judgeProviderId: "client-delegated"`) retrievable the same way as one produced via `run_ai_evaluation`.
- An invalid response returns `retry_needed` with a correction prompt (not an error) until `MaxAttempts` is exhausted, matching `AIJudgeEvaluationProvider`'s existing retry semantics exactly.
- `AutoAIEvaluationTrigger`'s automatic/background path is untouched — it keeps using a daemon-owned `IAIExecutionProvider`, since there's no live client to delegate to from a detached background task.
- The design has no Claude-specific assumption anywhere in the daemon: any MCP client that can call a tool, reason over returned text, and call a tool back satisfies the contract.

**Testing:** unit tests for the extracted builder/parser helpers and `DelegatedAIEvaluationService` (hand-rolled fakes, matching this repo's existing style — no SQLite/HTTP involved); TTL/single-use tests for the pending-evaluation store; Host-level end-to-end tests covering prepare→submit-valid, prepare→retry→submit-valid, and prepare→retry-exhaustion→502, matching `AIEvaluationEndpointsTests`' existing structure.

**Docs:** `docs/ai-evaluation.md` (already updated with the design above), ADR-0010 in `docs/architecture.md` (already recorded).

## Phase 13+ (each re-planned in detail when reached)

- Additional daemon-side context/metric plugins: Jira, GitHub, Azure DevOps, ADR/spec document providers (network-reachable ones only — filesystem-bound sources stay hook-pushed per ADR-0005/Phase 3).
- Observability: OpenTelemetry tracing/metrics across execution time, failure rates, scoring trends, recommendation accuracy, all queryable per-repo or across the whole daemon.
- Daemon lifecycle/admin conveniences (update, backup/restore the volume, inspect stored data) — likely a small admin skill rather than a new client, given Docker already covers start/stop/upgrade.
- Future Learning Pipeline epics: A/B testing, regression suites, prompt replay, synthetic benchmark generation, offline evaluation, continuous learning, knowledge graph integration, RAG over historical executions — all naturally cross-repo given the shared database.
- **Team-hosted mode** (multi-user, real auth/RBAC, network-exposed daemon) — explicitly deferred, not scheduled. Would require revisiting ADR-0005 (storage/multi-tenancy) and ADR-0007 (security) in `architecture.md`; the Domain/Application layers would not need to change.
- **Optional human evaluation, with fully automatic prompt updates.** A daemon-level config toggle (e.g. a `requireHumanEvaluation` flag alongside `ScoringConfig`) that lets a team skip `HumanEvaluation` (Phase 6) entirely rather than just zero-weighting it: `IScoringProvider` already supports computing `PromptScore` from AI evaluation + engineering metrics alone (Phase 8's zero-weight/missing-input edge case), so this mostly formalizes an existing capability into an explicit, discoverable setting rather than an implicit side effect of how weights happen to be configured. The new piece is closing the loop end-to-end: when the toggle is on and a `PromptScore` clears a configurable threshold (or a newer version's score beats the active one by a configurable margin), the daemon automatically creates/activates the improved `PromptVersion` (`parentVersionId` lineage, Phase 1/2) itself — no `/promptops rate` and no human sign-off required. This is the concrete, config-driven version of the "Automatic prompt refinement" / "continuous learning loop" items already in the unscheduled roadmap above and in `architecture.md` §7; teams that want a human in the loop keep today's default (toggle off), teams that don't can opt into it per daemon.

---

## End-of-Phase Ritual (every phase, starting Phase 1)

1. What was completed.
2. Design decisions made during implementation (and any deviation from this plan, with rationale).
3. Remaining work / known gaps.
4. Explicit request for approval before starting the next phase.
