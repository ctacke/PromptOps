# PromptOps — Phase 0: Architecture Decision Document

Status: **Draft for review** — no implementation has started. Nothing in this document should be treated as final until explicitly approved.

## 1. Problem Statement

PromptOps treats prompts as versioned engineering assets. It records how a prompt was used (context, execution, output), what engineering outcomes followed (build/test/Sonar/review metrics), how humans and AI judges rated the result, and uses all of that to score prompts and recommend better ones for future tasks. It is a telemetry and continuous-improvement platform for AI-assisted development, not a prompt library.

Core design constraint: **the core must not know about any concrete tool** (Claude Code, Jira, Sonar, GitHub, git, Azure DevOps, local LLMs). All integration happens through provider interfaces implemented as plugins.

**Distribution constraint:** PromptOps must be something you install into a target repo and have it just work — not a service someone has to stand up and operate. Concretely, that means: a single shared daemon runs once per developer machine (Docker), and each repo gets only a thin Claude Code plugin that talks to it over `localhost`. See §9 for the full rationale and §2 ADR-0009 for the packaging details.

## 2. Decision Records

### ADR-0001: Language and runtime — .NET 10 / C#

**Decision:** PromptOps core and application services are implemented in C# on .NET 10, packaged as a single Docker image (the "daemon" — see ADR-0009). No REST API is exposed publicly (see ADR-0006).

**Rationale:** The stated non-functional requirements (clean architecture, SOLID, DI, strong typing, comprehensive testing) map directly onto idiomatic .NET patterns. EF Core gives a swappable persistence layer without hand-rolling an ORM abstraction; the .NET plugin/assembly-loading model (`AssemblyLoadContext`) gives real isolation for third-party provider plugins, which a dynamically-typed runtime does not provide as cleanly. Packaging the daemon as a Docker image sidesteps per-OS/arch native-binary publishing entirely — one image runs anywhere Docker runs, which is a better fit for "install once per machine" than shipping platform-specific executables.

**Alternatives considered:**
- *TypeScript/Node.js* — better native fit with the Claude Code / MCP ecosystem (which is Node-based). Rejected as the *core* language because weak isolation between plugins and weaker compile-time guarantees work against the "core knows nothing about providers" goal. Not excluded from the system — the thin per-repo Claude Code plugin (ADR-0009) is plain shell/JSON, not compiled code, so this tradeoff mostly disappears at the distribution layer.
- *Python* — best fit if scoring/recommendation becomes ML-heavy. Rejected for the core for the same typing/DI reasons; may become the natural home for a future embeddings/ML microservice that the daemon calls internally over HTTP, keeping the same "core doesn't know about providers" boundary.
- *Self-contained native AOT binary per OS/arch* — considered as the packaging mechanism instead of Docker. Rejected: requires building and shipping separate artifacts per platform, versus one Docker image; Docker is also already the natural host for a long-running local daemon with a persistent volume.

**Consequence:** Provider plugins that need to shell out to Node/Python tools (e.g. a Claude Code provider) are still first-class — they're .NET assemblies inside the daemon image that invoke external processes/CLIs/APIs, same as any other integration.

### ADR-0002: Clean architecture with a plugin boundary

**Decision:** Four layers, dependencies point inward only:

```
Domain          → entities, value objects, domain events. No dependencies on anything.
Application     → use cases, provider interfaces (ports), DTOs. Depends only on Domain.
Infrastructure  → default/built-in implementations (SQLite repos, default context providers).
                  Depends on Application (implements its interfaces).
Plugins         → external integrations (Jira, Sonar, GitHub, Claude Code, Azure DevOps,
                  local LLMs). Each plugin is a separate assembly that depends only on
                  Application's interfaces — never on Infrastructure or on each other.
Host            → the daemon's composition root and entry point (runs inside the Docker
                  image — see ADR-0009). Wires Infrastructure + configured Plugins into
                  DI, hosts the MCP-over-HTTP endpoint and the local ingestion API.
                  Depends on Application (+ Infrastructure/Plugins only for registration).
```

**Enforcement:** Architecture fitness tests (NetArchTest or equivalent) run in CI from Phase 1 onward and fail the build if `Domain` or `Application` reference `Infrastructure`, any plugin, or any third-party SDK (EF Core's SQLite provider, Sonar client, Atlassian SDK, Octokit, etc.).

**Rationale:** This is the direct implementation of "core should know nothing about Claude Code / Jira / Sonar / GitHub / git / Azure DevOps / local LLMs" — enforced by the compiler and by CI, not just by convention.

### ADR-0003: Provider interfaces (ports)

All external capability is expressed as an interface in `Application`. Initial set:

| Interface | Responsibility | Example implementations |
|---|---|---|
| `IAIExecutionProvider` | Executes a resolved prompt against a specific AI backend, returns raw output + tool-usage trace | `ClaudeCodeProvider`, `ChatGptProvider`, `CopilotProvider`, `LocalModelProvider` |
| `IAIEvaluationProvider` | Asks an LLM judge whether AC/ADRs were satisfied and suggests prompt improvements | Reuses `IAIExecutionProvider` under the hood with a judge prompt template |
| `IContextProvider` | Gathers one facet of development context | `GitContextProvider`, `JiraContextProvider`, `AdrDocumentProvider`, `AzureBoardsContextProvider` |
| `IMetricCollector` | Collects one engineering metric source, keyed to an execution | `SonarMetricCollector`, `BuildResultCollector` (trx/junit + coverage), `ReviewMetricCollector` (GitHub/Azure DevOps PR data) |
| `IArtifactProvider` | Persists/retrieves large artifacts (diffs, logs, transcripts) outside the relational store | `BlobArtifactProvider` (filesystem/S3/Azure Blob) |
| `IScoringProvider` | Computes a `PromptScore` from weighted inputs under a `ScoringConfig` | `WeightedSumScoringProvider` (default, built into Infrastructure) |
| `IRecommendationProvider` | Ranks/searches historical prompt versions for a new task | `TagAndHistoryRecommendationProvider` (v1), `SemanticRecommendationProvider` (v2, Phase 10) |
| `IActivityClassifier` | Classifies a free-text task description into activity tags (e.g. "debugging", "code-authoring") before recommendation runs — the input `IRecommendationProvider.RecommendAsync(tags)` needs but that nothing else produces for a live session | `AIActivityClassifier` (built on `IAIExecutionProvider`, same pattern as `IAIEvaluationProvider` — no separate AI dependency) |
| `ISecretProvider` | Resolves credentials for plugins without the plugin owning secret storage | `EnvironmentSecretProvider` (default), `KeyVaultSecretProvider` |

Each interface is versioned independently (semantic versioning on the `Application` contracts package) so a plugin built against v1 doesn't silently break when v2 adds a method — new capabilities are added via new, optional interfaces (e.g. `IMetricCollectorV2 : IMetricCollector`) rather than breaking changes.

### ADR-0004: Plugin discovery and loading

**Decision:** Plugins are separate .NET class libraries dropped into a `plugins/` directory, each with a `plugin.manifest.json` declaring: name, version, supported interfaces, required configuration keys, required secret scopes. At startup, the host scans the plugin directory, loads each assembly into its own `AssemblyLoadContext`, and calls a required `IPromptOpsPlugin.Register(IServiceCollection, IConfiguration)` entry point. Which *configured* plugin instance actually services a given interface (e.g. which of three installed `IMetricCollector`s run) is controlled by `.promptops.yml`/`appsettings`, not by presence alone — multiple collectors of the same interface can be active simultaneously.

**Rationale:** Mirrors the extension model of VS Code/MSBuild SDK resolvers — well-understood, supports independent versioning/publishing per plugin, and keeps a broken third-party plugin from taking down the host process (isolated `AssemblyLoadContext`, caught at the `Register` boundary).

### ADR-0005: Storage strategy — SQLite, one database per machine

**Decision:** Repository pattern over EF Core, **SQLite only**. No LINQ/EF types leak past `Infrastructure` — `Application` only sees repository interfaces (`IPromptRepository`, `IExecutionRepository`, etc.) and plain DTOs/entities.

- **One database, owned by the daemon, shared across every repo on the developer's machine** — not one database per repo. The database file lives on a Docker named volume (e.g. `promptops-data:/data/promptops.db`) so it survives container restarts/upgrades.
- `ExecutionRecord.DevelopmentContext.repository` (already part of the domain model in §3) is what scopes usage history to a repo — `Prompt`/`PromptVersion` themselves are intentionally *not* repo-scoped, since a prompt is meant to be reusable across repos. Per-repo and cross-repo recommendation queries are both just a filter on `ExecutionRecord.repository`, not a schema difference. This is what makes cross-repo recommendations possible without any sync mechanism: there's only ever one database.
- **Prompt content**: stored as immutable text per version (not a blob store) — content is small, needs to be queryable/diffable, and versions are immutable once created.
- **Large artifacts** (full execution transcripts, diffs, logs): behind `IArtifactProvider`, default filesystem implementation writing into the same Docker volume. Kept out of the relational tables to avoid bloating them.
- **Vector/semantic search** (Phase 10+): in-process similarity (e.g. `sqlite-vec` or brute-force cosine over stored embeddings — single-machine, single-database scale makes brute-force entirely viable) behind an `IEmbeddingStore` port.

**Rationale:** This is a single long-running daemon on one developer's machine, not a multi-tenant service — Postgres would add an operational dependency (a database server to run) for no benefit at this scale. SQLite requires nothing to install or operate beyond the daemon itself, and EF Core's provider model still gives a clean seam (`IPromptRepository` etc.) if a different single-file or server-backed store is ever warranted later — but that's not a design goal being actively built toward, unlike in the original service-oriented draft of this document.

**Explicitly rejected:** PostgreSQL, per-repo isolated databases (see §9 for why per-repo isolation was rejected), a central multi-user database (see §9 — deferred, not built now).

### ADR-0006: Interface style — MCP over HTTP + a loopback ingestion API (no public REST API)

**Decision:** The daemon exposes exactly two surfaces, both bound to `localhost` only:

- **MCP over HTTP** (streamable HTTP transport) — registered once as a *remote* MCP server in Claude Code (`claude mcp add --transport http promptops http://localhost:<port>/mcp`, or automated by the plugin's install step). This gives the agent live tools mid-session: search history, get a recommendation, submit a rating — without installing any local MCP binary, since the daemon itself speaks MCP over the network.
- **A minimal local HTTP ingestion API** — the contract between per-repo Claude Code hooks (plain shell scripts) and the daemon. Hooks `curl` this to push captured git context (`SessionStart`), tool-usage events (`PreToolUse`/`PostToolUse`), and finalize an execution with diff stats (`Stop`). This is not a public API for arbitrary external clients — it exists purely so hooks have something simple to call.

**Rationale:** The original draft of this document assumed a REST API as the system's primary interface, serving a future CLI/Web UI/VS Code/JetBrains/desktop/mobile client roster. That framing assumed a centrally-hosted, always-reachable service. The actual system is a daemon that lives on one developer's machine — its only real clients are Claude Code itself (via MCP) and the shell hooks the plugin installs into each repo. A public REST API with OpenAPI/Swagger and API-key auth is unnecessary surface area for that shape of system; if a future client (e.g. a local dashboard) needs one, it can be added as an additive interface without touching `Application`.

**Explicitly rejected (for now):** a public-facing REST API, OAuth2/OIDC, API-key auth — none of these make sense for a loopback-only, single-user daemon. Revisit only if the "team-hosted service" direction (§9) is ever pursued.

### ADR-0007: Security posture

- **The daemon binds to `localhost` only** — never exposed on the network, no port forwarding, no TLS needed for a loopback-only surface. This is the primary security property of the whole design: there is no remote attack surface because there is no remote surface, period.
- **Secrets** never live in PromptOps config directly — `ISecretProvider` resolves them at runtime (env vars passed into the container, or a mounted secrets file). Plugin manifests declare required secret scopes so the host can refuse to load a plugin missing its secrets rather than failing at first use. Because metric-collector plugins (Sonar, Jira, etc.) may need different credentials per repo, secrets are resolvable per-`repository`, not just globally.
- **Captured context may contain proprietary source/IP from multiple repos in one database** — this is the main risk introduced by the shared-daemon model (§9): a compromise of the daemon/volume exposes history from every repo on the machine, not just one. Mitigations: encryption at rest for sensitive fields (prompt inputs/outputs, diffs) on the SQLite database file/volume, and an explicit PII/secret-scrubbing hook (`IContentSanitizer`) run over captured context *before* it's persisted or indexed, so leaked credentials in a diff don't end up in the knowledge base.
- **No RBAC/multi-user auth** — deliberately out of scope. There is exactly one user (the developer running the daemon on their own machine). Ownership/team fields on `Prompt` are metadata for future use, not an enforced access-control boundary today.
- **Audit trail**: every mutation (prompt edit, score recompute, evaluation submission) raises a domain event persisted to an append-only audit table — who changed what, when, in which repo. Useful for debugging and for a future team-hosted mode, even though nothing enforces access based on it yet.
- **No arbitrary code execution in core.** `IAIExecutionProvider` implementations may shell out to external tools (that's expected — that's what a Claude Code provider does), but the daemon process itself never `eval`s or executes model output.

### ADR-0008: Extensibility beyond providers

Two additional extension points beyond the provider interfaces, needed for the roadmap items (A/B testing, replay, synthetic benchmarks):
- **Domain events** (`ExecutionRecorded`, `MetricsCollected`, `EvaluationSubmitted`, `ScoreComputed`) published via an in-process publisher so future features (e.g. a regression-suite runner) subscribe without modifying the phase that produced the event.

  **Amendment (Phase 3):** originally specified as MediatR. Before adding the package, its license was checked directly (`LICENSE.md` bundled in the NuGet package) rather than assumed — MediatR 14.x is dual-licensed under RPL 1.5 (copyleft: distributing software built with it obligates releasing that software's own source under the same terms) or a paid commercial license. Both are incompatible with this project's "open-source friendly" requirement, so it was dropped in favor of a small hand-rolled dispatcher: `Domain` defines a framework-free `IDomainEvent` marker + `AggregateRoot` (accumulates events), `Application` defines `IDomainEventHandler<TEvent>`/`IDomainEventPublisher`, and `DomainEventPublisher` resolves handlers via DI reflectively. No pipeline behaviors, no request/response mediation — just publish-to-registered-handlers, which is all that's needed today. See `docs/execution-tracking.md` for the implementation.
- **Configuration-driven scoring** — `ScoringConfig` weights are data, not code, so new scoring strategies are a config change, not a deploy, for the common case (only genuinely new *inputs* require code).

### ADR-0009: Distribution and packaging — Docker daemon + thin Claude Code plugin

**Decision:** Two independently-versioned artifacts:

1. **Core daemon** — a single Docker image containing the full .NET 10 application (Domain/Application/Infrastructure/Host, all provider plugins). Started once per developer machine (`docker run` or a small `docker-compose.yml`), not once per repo. Owns the SQLite volume (ADR-0005) and exposes MCP-over-HTTP + the loopback ingestion API (ADR-0006).
2. **Per-repo artifact: a Claude Code plugin**, installed into each target repo the same way `context-mode` is installed in this environment (marketplace/git reference). It contains **no compiled code of its own** — just:
   - `plugin.json` manifest
   - `hooks/` — plain shell scripts (`SessionStart`, `PreToolUse`/`PostToolUse`, `Stop`) that `curl` the daemon's loopback ingestion API
   - a one-time MCP remote-server registration step pointing at the daemon
   - `skills/`/slash commands (`/promptops rate`, `/promptops recommend`, `/promptops history`) that call the daemon's API

**Install flow:** first-time setup on a machine = start the daemon once (a plugin-provided helper script/skill can offer to `docker run` it if it isn't already running). After that, "installing PromptOps into a repo" is just installing the thin plugin — no build step, no per-repo daemon, no compiled binary.

**Rationale:** This directly satisfies the original requirement — "a framework I can copy/install into another repo and have it work" — while keeping the heavyweight, stateful part (the daemon) a one-time, machine-level concern rather than something repeated per repo. It also keeps the plugin package tiny and language-agnostic (shell + JSON), independent of whatever language the daemon itself is written in.

**Explicitly rejected:** shipping a compiled binary inside the plugin (rejected in ADR-0001 in favor of Docker); a per-repo container (rejected in §9 — it would forfeit cross-repo recommendations, the main reason the shared-daemon model was chosen).

### ADR-0010: Client-Delegated AI Evaluation (MCP sampling is gone)

**Context:** `IAIEvaluationProvider` (ADR-0003, Phase 7) is built on `IAIExecutionProvider` — a judge is "just another prompt execution." That assumed *something* would eventually call a real model on the daemon's behalf: MCP originally specified `sampling/createMessage`, letting a server ask the connected client to run a completion using whatever model/session the client already has. That's exactly the shape PromptOps needs — the daemon judging an execution shouldn't require its own separate AI credentials when it's being driven by a client (Claude Code, or any other MCP client) that already has an authenticated model conversation open. But sampling was deprecated from the MCP spec (SEP-2577) before any mainstream client — including Claude Code — implemented the client side of it. It cannot be built on.

**Decision:** Add a second, additive way to run `IAIEvaluationProvider`'s judge logic — **client-delegated evaluation** — that gets the same effect via two ordinary MCP tool calls instead of a protocol-level sampling request:

1. `prepare_ai_evaluation(executionId)` — the daemon builds the judge prompt (extracted from `AIJudgeEvaluationProvider` into shared, dependency-free `JudgePromptBuilder`/`JudgeResponseParser` helpers so both paths use identical prompt/schema logic) and returns it, tagged with a short-lived, single-use `correlationId`. It does **not** call any model.
2. The calling agent — whatever MCP client is in the conversation — answers that prompt using its own current reasoning/model, exactly as it would answer any other request in the same session. No new credentials, no subprocess, no separate API key.
3. `submit_ai_evaluation_result(correlationId, response)` — the daemon validates/parses the answer against the judge schema (same tolerant extraction + retry-with-correction loop `AIJudgeEvaluationProvider` already has, just restructured as request/response instead of an internal loop) and persists the resulting `AIEvaluation` exactly as `run_ai_evaluation` does today. A schema mismatch returns `retry_needed` with a correction prompt instead of throwing, so the client can just try again in the same turn.

This is deliberately **transport-agnostic at the protocol level** — it's built entirely from ordinary MCP tool calls, not a new MCP capability — so it doesn't depend on Claude Code specifically or on any future sampling-equivalent landing. Any MCP client capable of (a) calling a tool, (b) reasoning over the returned text with its own model, (c) calling a tool back with the result satisfies the contract. `docs/ai-evaluation.md` has the full design (pending-evaluation lifetime, correlation-token handling, MCP tool contracts, the updated `/promptops evaluate` flow).

**What this doesn't replace:** `IAIExecutionProvider`/`IAIEvaluationProvider`/`run_ai_evaluation` (daemon-owned, autonomous judging) stay exactly as they are. `AutoAIEvaluationTrigger` runs in a detached background task with no live client attached to delegate to — it has nothing to delegate *to*, so automatic/unattended evaluation still needs a daemon-owned `IAIExecutionProvider` (`ManualAIExecutionProvider` for tests; a real backend such as `ClaudeCliAIExecutionProvider`, shelling out to a locally-installed, locally-authenticated `claude` CLI, for real unattended use). Client delegation is the new *preferred* path specifically for the interactive `/promptops evaluate` case, where a live client session already exists — it doesn't need to become the only path.

**Explicitly rejected:**
- *Rebuilding on MCP sampling* — dead per SEP-2577; no mainstream client (including Claude Code) implements the client side.
- *Requiring the daemon to hold its own provider-specific API keys as the default path* — technically simplest, but violates "never require users to duplicate credentials that already exist in their AI client if that can reasonably be avoided." Kept available (`IAIExecutionProvider` + `AIExecution:Provider` config, ADR-0003) as an opt-in/automatic-path backend, not the default interactive path.
- *A new bespoke callback protocol (WebSocket push, long-poll) instead of two plain MCP tool calls* — unnecessary complexity for a flow that completes within one conversation turn; ordinary request/response tool calls are sufficient and keep the daemon's two surfaces (ADR-0006) unchanged.

**Amendment (Phase 13): a second delegation mechanism for the automatic/unattended case.** ADR-0010's original delegation flow requires a live, mid-conversation agent turn to answer `prepare_ai_evaluation` — it has nothing to offer `AutoAIEvaluationTrigger`, which fires from a detached background task with no conversation attached. Rather than falling back to a daemon-owned `IAIExecutionProvider` (credentials living in the daemon/container) for unattended evaluation, Phase 13 delegates from the **per-repo plugin's `SessionEnd` hook** instead of the daemon: the hook (already a local Node.js script, ADR-0009) calls the same `prepare`/`submit` capability — now also exposed as loopback ingestion-API endpoints, not just MCP tools, since hooks aren't MCP clients — and shells out to the locally-installed, already-authenticated `claude` CLI itself as a **detached subprocess** to answer the prompt. This reuses the developer's own machine-level Claude Code login (the same credential-avoidance goal as the original decision above), without installing anything in the daemon's container or mounting any credentials into it — the daemon stays exactly as unaware of any specific AI client as ADR-0002 requires. See `docs/ai-evaluation.md` for the full hook-side design.

## 3. Domain Model

```
Prompt (aggregate root)
 ├─ id, name, category, owners[], tags[], createdAt
 └─ PromptVersion[] (immutable once created)
     ├─ id, promptId, versionNumber, content, templateVariables[]
     ├─ parentVersionId (lineage), changelogEntry, status (Draft|Active|Deprecated)
     ├─ createdBy, createdAt
     └─ PromptDependency[] (→ other PromptVersion ids, relationship type)

PromptMetadata (stored separately from PromptVersion.content by requirement)
 └─ promptId, description, tags[], categories[], owners[], externalRefs[]

ExecutionRecord
 ├─ id, promptVersionId, timestamp, developerId
 ├─ DevelopmentContext (value object, assembled from IContextProvider results):
 │    repository, branch, commit, taskId, referencedDocuments[], referencedADRs[],
 │    acceptanceCriteria[], languages[] (hook-detected, e.g. "csharp" — Phase 3/4b)
 ├─ inputs (json), output (text or IArtifactProvider ref for large output)
 ├─ executionTimeMs, aiProviderId, model, modelParameters (json)
 ├─ toolUsage[] (name, count, durationMs)
 └─ filesChanged[], linesAdded, linesDeleted

EngineeringMetrics (0..n per ExecutionRecord — arrive asynchronously over time)
 └─ executionId, collectedBy (IMetricCollector source), collectedAt,
    buildSuccess, testSuccess, coverage, sonarIssues, warnings, codeSmells,
    securityFindings, duplication, cyclomaticComplexity, reviewComments,
    reviewIterations, mergeTimeMinutes, rollbackNeeded, manualEdits

HumanEvaluation
 └─ executionId, evaluatorId, correctness, helpfulness, architecture, readability,
    completeness, hallucinations, confidence, overallSatisfaction, notes, timestamp

AIEvaluation (stored separately from HumanEvaluation by requirement)
 └─ executionId, judgeProviderId, judgeModel, satisfiesAcceptanceCriteria,
    adrViolations[], ignoredRequirements[], unnecessaryComplexityNotes,
    suggestedPromptImprovements[], rawResponse, timestamp

ScoringConfig
 └─ id, name, version, weights { humanRating, sonar, tests, build,
    acceptanceCriteria, manualFixes, reviewComments, regressionBugs }

PromptScore
 └─ promptVersionId, scoringConfigId, computedAt, overallScore,
    componentScores (json breakdown), sampleSize

Recommendation (query-time result, not necessarily persisted long-term)
 └─ queryContext, recommendedPromptVersionId, rationale, similarityScore, rank
```

Key invariant: `PromptVersion` content is immutable once created (edits create a new version with `parentVersionId` set) — this is what makes "prompts as versioned engineering assets" true rather than aspirational.

## 4. Component Diagram

One Docker daemon per developer machine; any number of repos, each carrying only the thin plugin, all talking to the same daemon over `localhost`.

```
   Repo A                    Repo B                    Repo C
┌───────────┐             ┌───────────┐             ┌───────────┐
│ PromptOps  │             │ PromptOps  │             │ PromptOps  │
│ Claude Code│             │ Claude Code│             │ Claude Code│
│  plugin    │             │  plugin    │             │  plugin    │
│ (manifest, │             │ (manifest, │             │ (manifest, │
│  hooks,    │             │  hooks,    │             │  hooks,    │
│  skills —  │             │  skills —  │             │  skills —  │
│  no compiled│            │  no compiled│            │  no compiled│
│  code)      │            │  code)      │            │  code)      │
└─────┬──────┘             └─────┬──────┘             └─────┬──────┘
      │ curl (hooks)              │ curl (hooks)              │ curl (hooks)
      │ MCP over HTTP (agent)     │ MCP over HTTP (agent)     │ MCP over HTTP (agent)
      └─────────────────┬─────────┴────────────┬───────────────┘
                         │        localhost      │
                 ┌───────▼────────────────────────▼───────┐
                 │        PromptOps Daemon (Docker)         │
                 │   Host: MCP-over-HTTP + ingestion API    │
                 └───────────────────┬───────────────────────┘
                                      │
                 ┌───────────────────▼───────────────────────┐
                 │           PromptOps.Application             │
                 │  Use cases · provider interfaces (ports)    │
                 │  Domain events · ScoringConfig evaluation   │
                 └──┬───────────┬───────────┬───────────┬──────┘
                    │           │           │           │
         ┌──────────▼──┐ ┌─────▼──────┐ ┌──▼────────┐ ┌▼──────────────┐
         │  Domain      │ │Infrastructure│ │ Plugins   │ │ Plugins        │
         │  entities,   │ │(EF Core/    │ │ (Sonar,   │ │ (Claude Code,  │
         │  value       │ │ SQLite,     │ │  Jira,    │ │  ChatGPT,      │
         │  objects     │ │ default Git │ │  GitHub,  │ │  Copilot,      │
         │  (no deps)   │ │ context,    │ │  Azure    │ │  local LLMs)   │
         │              │ │ default     │ │  DevOps)  │ │                │
         │              │ │ scoring)    │ │           │ │                │
         └──────────────┘ └─────┬────────┘ └───────────┘ └────────────────┘
                                 │
                          ┌──────▼──────┐
                          │  Docker      │
                          │  volume:     │
                          │  SQLite +    │
                          │  artifacts   │
                          │  (+ embedded │
                          │  similarity  │
                          │  search,     │
                          │  Phase 10+)  │
                          └─────────────┘
```

## 5. Data Flow (end-to-end execution → recommendation)

1. Developer starts a task in Claude Code in some repo. A `SessionStart` hook calls the daemon's ingestion API to resolve the `PromptVersion` to use (manually chosen, or the top `Recommendation` — which may draw on history from *any* repo on the machine, not just this one) and opens an `ExecutionRecord`.
2. `IContextProvider`s run inside the daemon (git info passed up from the hook, Jira, ADR/doc lookup) and assemble `DevelopmentContext`, tagged with this repo.
3. `IAIExecutionProvider` (Claude Code, running locally as normal) does the actual work; `PreToolUse`/`PostToolUse` hooks stream tool-usage stats to the daemon; the `Stop` hook finalizes the `ExecutionRecord` with diff stats (files changed, lines added/removed), publishing `ExecutionRecorded`.
4. Asynchronously, as CI runs: `IMetricCollector`s (Sonar, build/test, review) post `EngineeringMetrics` keyed to the `ExecutionRecord`, each publishing `MetricsCollected`. These can arrive minutes to days apart (merge time, rollback data) — metrics are additive, not a single snapshot.
5. A human optionally submits a `HumanEvaluation` via a `/promptops rate` slash command (which calls the daemon over MCP or the ingestion API).
6. The AI evaluation pipeline (`IAIEvaluationProvider`, itself built on `IAIExecutionProvider`) runs against AC/ADR references and produces an `AIEvaluation`, stored separately.
7. `IScoringProvider` recomputes `PromptScore` for the `PromptVersion` (triggered by any of the above events, debounced) using the active `ScoringConfig`.
8. `IRecommendationProvider` indexes the execution + score into the shared knowledge base (tags now, embeddings from Phase 10) so the next similar task — in *this* repo or any other repo on the machine — surfaces this prompt version, or avoids it if it scored poorly.

## 6. Extensibility Strategy Summary

- New tool integration = new plugin assembly + manifest, loaded inside the daemon. Zero core changes, zero change to the per-repo plugin.
- New scoring input = new field on `EngineeringMetrics`/`HumanEvaluation` (additive) + new `ScoringConfig` weight key. Existing configs keep working (missing weight = 0 contribution).
- New AI backend = new `IAIExecutionProvider`. Because `IAIEvaluationProvider` is built on the same abstraction, "use GPT-4 to judge Claude's output" works with no extra code.
- New client = a new MCP-over-HTTP or ingestion-API consumer. The daemon never depends on a specific client; the per-repo plugin is just one such consumer.

## 7. Future Roadmap (unscheduled, informs interface design now so we don't paint ourselves into a corner)

Automatic prompt refinement · Prompt A/B testing · Regression suites · Prompt replay · Synthetic benchmark generation · Offline evaluation · Continuous learning loop · Knowledge graph integration · RAG over historical executions · a possible future team-hosted mode (§9).

All of these consume the same event stream (`ExecutionRecorded`, `MetricsCollected`, `ScoreComputed`) and the same `IRecommendationProvider`/`IScoringProvider` ports — they are additive subscribers, not architectural changes.

## 8. Explicitly Out of Scope for Phase 0

- Choice of specific ORM query patterns / migration tooling details (decided in Phase 1/2 implementation, not architecture).
- UI/UX design for any client.
- CI/CD pipeline for PromptOps' own build (separate concern from the product).
- **Multi-user/team hosting, a network-exposed daemon, cross-machine sync, and auth/RBAC across users.** These were explicitly considered (see §9) and deliberately deferred — not a gap, a scope decision. The daemon is single-user, single-machine, loopback-only by design.

## 9. Deployment Model Decision Record

This section exists because the deployment model went through three iterations during design and the reasoning is easy to lose without it written down.

1. **First pass:** centrally-hosted service (ASP.NET Core + PostgreSQL), any number of repos/developers as network clients. Rejected — the actual goal is something installed *into* a repo with nothing to stand up or operate.
2. **Second pass:** fully isolated, per-repo install — a self-contained native binary + SQLite file scoped to a single repo, zero data sharing even across a developer's own repos. This satisfies "copy/install into a repo" literally, but has a real cost: `IRecommendationProvider` ("what prompt worked best for problems like this") is blind on every new repo, which is exactly when it would be most valuable — new projects have the least accumulated history to learn from.
3. **Final decision: a single shared local daemon per developer machine** (this document). Every repo's plugin talks to the same daemon over `localhost`. Task patterns rhyme across a given developer's projects, so pooling execution history means recommendations are useful on day one of a brand-new repo, not just after that repo alone accumulates enough runs. The daemon never leaves the machine and there is no multi-user concept — this is not the centralized service from pass 1, it's "isolated per repo" traded for "isolated per developer machine" specifically to make the recommendation engine work.

A fourth option — a team-hosted service multiple developers point at — was considered and explicitly deferred (not rejected outright; §7 roadmap). It would recover org-wide learning at the cost of the auth/RBAC/multi-tenancy/ops surface this design currently avoids entirely. Revisiting it later is possible without a rewrite: the daemon's internal architecture (Domain/Application/Infrastructure, provider interfaces) doesn't change — only ADR-0005 (storage: would need real multi-tenancy) and ADR-0007 (security: would need real auth) would need revisiting.
