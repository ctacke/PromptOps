# Contributing to PromptOps

This covers the project's status, solution layout, and how to build/test it locally. If you're looking to *use* PromptOps rather than work on it, see [`README.md`](README.md) and [`docs/getting-started.md`](docs/getting-started.md) instead.

## Status

Phase 0 (architecture decision + project plan), Phase 1 (domain core + solution skeleton), Phase 2 (prompt repository — EF Core/SQLite persistence, see [`docs/prompt-repository.md`](docs/prompt-repository.md)), Phase 3 (execution tracking — see [`docs/execution-tracking.md`](docs/execution-tracking.md)), Phase 4a (Docker daemon packaging + MCP-over-HTTP — see [`docs/daemon-setup.md`](docs/daemon-setup.md)), Phase 4b (the per-repo Claude Code plugin — see [`docs/installing-promptops.md`](docs/installing-promptops.md)), Phase 5 (engineering metric collectors — Sonar + build/test results, real plugin loading — see [`docs/metrics.md`](docs/metrics.md)), Phase 6 (human evaluation — `/promptops rate`, see [`docs/human-evaluation.md`](docs/human-evaluation.md)), Phase 7 (AI evaluation pipeline — schema-validated judge output with retry, see [`docs/ai-evaluation.md`](docs/ai-evaluation.md)), Phase 8 (scoring engine — weighted-sum `PromptScore` with debounced recompute-on-event, see [`docs/scoring.md`](docs/scoring.md)), Phase 9 (recommendation engine v1 — classify-then-recommend, tag + historical ranking across every repo, `/promptops recommend`, see [`docs/recommendations.md`](docs/recommendations.md)), Phase 10 (semantic search / knowledge base — embedding index, recommendation engine v2 blending semantic + tag + historical ranking, see [`docs/knowledge-base.md`](docs/knowledge-base.md)), Phase 11 (optional human evaluation / automatic prompt promotion — manual and score-driven `PromptVersion` activation, see [`docs/promotion-policy.md`](docs/promotion-policy.md)), Phase 15 (execution attribution + proactive recommendation — a `UserPromptSubmit` hook attributes each session to a real prompt version, capturing a new one for a novel development activity, see [`docs/execution-attribution.md`](docs/execution-attribution.md)), Phase 16a (automatic prompt refinement — an `AIEvaluationRecorded` handler drafts an improved `PromptVersion` from the AI judge's suggestions), Phase 16b (synthetic-benchmark pre-screen — a drafted refinement is graded against generated inputs and deprecated if it regresses, before it can reach real work), and Phase 16c (A/B shadow adoption — ε-greedy live traffic lets a benchmark-passing draft earn a real score, which the existing auto-promotion gate acts on, closing the self-improvement loop; see [`docs/prompt-refinement.md`](docs/prompt-refinement.md)) are complete. Each subsequent phase ships working software and is reviewed before the next begins — see [`docs/project-plan.md`](docs/project-plan.md) for the current phase breakdown.

## Solution layout

```
PromptOps.slnx
src/
  PromptOps.Domain          entities/value objects, zero dependencies (ADR-0002)
  PromptOps.Application     use cases, provider interfaces/ports (ADR-0003), depends only on Domain
  PromptOps.Infrastructure  default implementations of Application's ports (EF Core/SQLite, etc.)
  PromptOps.Host            the daemon's composition root and entry point (ADR-0009)
plugins/
  PromptOps.Plugin.Sdk         IPromptOpsPlugin — the contract daemon-side provider plugins implement
  PromptOps.Plugins.Sonar      IMetricCollector querying SonarQube/SonarCloud (Phase 5)
  PromptOps.Plugins.BuildResult IMetricCollector parsing pushed trx/Cobertura content (Phase 5)
tests/
  PromptOps.Domain.Tests         unit tests for Domain
  PromptOps.Architecture.Tests   NetArchTest fitness tests enforcing the layering rule above
  PromptOps.Infrastructure.Tests integration tests against real SQLite (SqliteFixture)
  PromptOps.Plugins.Tests        unit tests for the Sonar/BuildResult collectors
  PromptOps.Host.Tests           integration tests against the Host (WebApplicationFactory) + plugin loading
claude-plugin/
  hooks/    SessionStart/UserPromptSubmit/PreToolUse/PostToolUse/SessionEnd — capture + attribute execution context automatically
  skills/   /promptops init|rate|evaluate|recommend|history|setup — anything that needs a human
```

Dependencies point inward only: `Domain` ← `Application` ← `Infrastructure`/`Host`. `PromptOps.Architecture.Tests` fails the build if that's ever violated — see [`docs/architecture.md`](docs/architecture.md) ADR-0002.

## Building and testing

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```
dotnet build
dotnet test
```

Both run against every project in the solution; no external services are required for these. Docker is only needed to run the daemon itself — see [`docs/daemon-setup.md`](docs/daemon-setup.md).

## Where to go next

- [`docs/architecture.md`](docs/architecture.md) — ADRs and full design rationale.
- [`docs/project-plan.md`](docs/project-plan.md) — the phased implementation plan, including what's scheduled but not yet built.
- Every phase's own doc (`docs/*.md`) covers its domain model, testing approach, and acceptance criteria in detail.
