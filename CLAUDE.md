# PromptOps — project instructions

PromptOps is prompt versioning, evaluation, and continuous improvement for AI-assisted
development. It runs as a local Docker daemon (`ghcr.io/ctacke/promptops`) exposing MCP tools
and slash commands (`/promptops init|rate|evaluate|recommend|history|setup`) to any repo that
installs the Claude Code plugin under `claude-plugin/`.

Use normal tools here — Bash, Read, Grep, Glob, WebFetch — the same as any other repo. Don't
route tool calls through context-mode or another plugin unless explicitly asked to.

## Solution layout

```
PromptOps.slnx
src/
  PromptOps.Domain          entities/value objects, zero dependencies (ADR-0002)
  PromptOps.Application     use cases, provider interfaces/ports (ADR-0003), depends only on Domain
  PromptOps.Infrastructure  default implementations of Application's ports (EF Core/SQLite, etc.)
  PromptOps.Host            the daemon's composition root and entry point (ADR-0009)
plugins/
  PromptOps.Plugin.Sdk          IPromptOpsPlugin — the contract daemon-side provider plugins implement
  PromptOps.Plugins.Sonar       IMetricCollector querying SonarQube/SonarCloud
  PromptOps.Plugins.BuildResult IMetricCollector parsing pushed trx/Cobertura content
tests/
  PromptOps.Domain.Tests         unit tests for Domain
  PromptOps.Architecture.Tests   NetArchTest fitness tests enforcing the layering rule below
  PromptOps.Infrastructure.Tests integration tests against real SQLite (SqliteFixture)
  PromptOps.Plugins.Tests        unit tests for the Sonar/BuildResult collectors
  PromptOps.Host.Tests           integration tests against the Host (WebApplicationFactory) + plugin loading
claude-plugin/
  hooks/    SessionStart/PreToolUse/PostToolUse/SessionEnd — capture execution context automatically
  skills/   /promptops init|rate|evaluate|recommend|history|setup — anything that needs a human
```

Dependencies point inward only: `Domain` ← `Application` ← `Infrastructure`/`Host`.
`PromptOps.Architecture.Tests` fails the build if that's ever violated — see
[`docs/architecture.md`](docs/architecture.md) ADR-0002.

## Building and testing

Requires the .NET 10 SDK.

```
dotnet build
dotnet test
```

Both run against every project in the solution; no external services required. Docker is only
needed to run the daemon itself — see [`docs/daemon-setup.md`](docs/daemon-setup.md). After
changing daemon/provider code, rebuild and restart the running container to pick it up:

```
docker compose up -d --build
```

## Docs

- [`docs/architecture.md`](docs/architecture.md) — ADRs and design rationale.
- [`docs/project-plan.md`](docs/project-plan.md) — phased implementation plan.
- Every phase's own doc (`docs/*.md`) covers its domain model and testing approach in depth.
- [`CONTRIBUTING.md`](CONTRIBUTING.md) — project status and full contributor guide.

## Git & PRs

- Don't commit or push unless explicitly asked.
- Work on a branch, never commit straight to `main`.
