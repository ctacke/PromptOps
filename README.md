# PromptOps

Prompt versioning, evaluation, and continuous improvement for AI-assisted development.

## The problem

As a developer works — writing code, running tests, debugging, reviewing — the prompts and instructions given to an AI coding assistant naturally fall into different *activities* (fix a bug, write a test, review a PR, scaffold an endpoint). An assistant gives better results when the prompt for a given activity is tailored to that kind of work, and better still when that prompt keeps improving as it gets used more — shaped by what has actually worked, not just whatever a developer happened to type that day.

Today that improvement, if it happens at all, lives entirely in a developer's head: a vague sense that "this phrasing tends to work better," rediscovered by feel, forgotten between sessions, and lost every time work moves to a new project.

PromptOps treats prompts as versioned engineering assets and measures their effectiveness using evidence — build success, test results, static analysis findings, review iterations — alongside structured human and AI evaluation, instead of relying purely on gut feel about whether an interaction "went well." Because that evidence is captured per developer rather than locked to a single repository, none of it is lost when starting a new project: a prompt version that worked well on one codebase, and the evidence for *why* it worked, is immediately available as a starting point on the next one.

## What PromptOps is — and isn't

**It is** an engineering telemetry platform for AI-assisted development: versioned prompts, a record of every execution and the context around it, objective engineering metrics, human and AI evaluation, and a scoring/recommendation engine that surfaces which prompt version to reach for next.

**It is not** a prompt library or snippet manager — saving and tagging prompt text is a small part of it, not the point. **It also does not (yet) rewrite prompts for you** — today it measures and recommends; a human decides what to change. Automatic prompt refinement is on the roadmap, not something built into the first phases.

## How it works

PromptOps runs as a small local daemon (Docker) on your machine — started once, not per project — that owns your prompt history and evaluation data. Each repository gets a thin Claude Code plugin: hooks that capture context and execution data automatically, and slash commands (`/promptops rate`, `/promptops recommend`, `/promptops history`) for anything that needs a human. Because every repo talks to the same local daemon, a recommendation on a brand-new project can draw on history from every other project on your machine — nothing is siloed per repo, and nothing leaves your machine.

See [`docs/architecture.md`](docs/architecture.md) for the full architecture and design rationale, and [`docs/project-plan.md`](docs/project-plan.md) for the phased implementation plan.

## Goals

- Version prompts and track their evolution over time
- Record every prompt execution along with the development context around it (repo, branch, commit, task, referenced docs/ADRs, acceptance criteria)
- Automatically collect objective engineering metrics (build, test, coverage, static analysis, review activity) where possible
- Capture structured human evaluation and AI-judged evaluation, kept separate from each other
- Score prompt effectiveness from configurable, weighted combinations of the above
- Recommend the best-performing prompt for a given kind of task, drawing on history across all of a developer's projects
- Support multiple AI providers (Claude Code today; ChatGPT, Copilot, local models by design, not by hardcoding)

## Status

Phase 0 (architecture decision + project plan), Phase 1 (domain core + solution skeleton), Phase 2 (prompt repository — EF Core/SQLite persistence, see [`docs/prompt-repository.md`](docs/prompt-repository.md)), and Phase 3 (execution tracking — see [`docs/execution-tracking.md`](docs/execution-tracking.md)) are complete. Each subsequent phase ships working software and is reviewed before the next begins — see [`docs/project-plan.md`](docs/project-plan.md) for the current phase breakdown.

## Solution layout

```
PromptOps.slnx
src/
  PromptOps.Domain          entities/value objects, zero dependencies (ADR-0002)
  PromptOps.Application     use cases, provider interfaces/ports (ADR-0003), depends only on Domain
  PromptOps.Infrastructure  default implementations of Application's ports (EF Core/SQLite, etc.)
  PromptOps.Host            the daemon's composition root and entry point (ADR-0009)
plugins/
  PromptOps.Plugin.Sdk      IPromptOpsPlugin — the contract daemon-side provider plugins implement
tests/
  PromptOps.Domain.Tests        unit tests for Domain
  PromptOps.Architecture.Tests  NetArchTest fitness tests enforcing the layering rule above
  PromptOps.Host.Tests          integration tests against the Host (WebApplicationFactory)
```

Dependencies point inward only: `Domain` ← `Application` ← `Infrastructure`/`Host`. `PromptOps.Architecture.Tests` fails the build if that's ever violated — see [`docs/architecture.md`](docs/architecture.md) ADR-0002.

## Building and testing

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```
dotnet build
dotnet test
```

Both run against every project in the solution; no external services (no database, no Docker) are required yet — that starts changing from Phase 2 onward.
