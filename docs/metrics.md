# Engineering Metrics (Phase 5)

`EngineeringMetrics` is what turns an `ExecutionRecord` into evidence, not just a log entry — objective build/test/static-analysis facts that later phases (8: scoring, 9: recommendation) weigh against human/AI evaluation.

## Domain model

```
EngineeringMetrics (independent aggregate, 0..n per ExecutionRecord)
 ├─ id, executionId, collectedBy, collectedAt
 ├─ buildSuccess, testSuccess, coverage
 ├─ sonarIssues, warnings, codeSmells, securityFindings, duplication, cyclomaticComplexity
 └─ reviewComments, reviewIterations, mergeTimeMinutes, rollbackNeeded, manualEdits
```

Like `ExecutionRecord.PromptVersionId`, `EngineeringMetrics.ExecutionId` is a plain value, not a foreign key — a collector reporting metrics never requires the execution to be loaded in the same transaction. Every row is immutable and additive: a collector run creates a **new** row rather than updating a shared one, because metrics genuinely "arrive asynchronously over time" from independent sources (a Sonar scan, a CI run) and a later run should never silently overwrite an earlier one's numbers. Querying `GET /executions/{id}/metrics` returns the full history, not a single merged snapshot — reconciling multiple rows into one number is Phase 8's job (`IScoringProvider`), not this layer's.

All fields are nullable. No single collector populates all of them — see the collector table below for which fields each one actually produces. A missing field means "not measured," not zero.

## `IMetricCollector`

```csharp
public interface IMetricCollector
{
    string Name { get; }
    Task<EngineeringMetrics?> CollectAsync(Guid executionId, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken = default);
}
```

Returning `null` means "nothing to report this call" — not an error. This is what lets `MetricsCollectionService` fan a single request out to *every* registered collector without any of them needing to know about the others: a collector that doesn't recognize what's in `parameters`, or isn't configured, just returns `null` and is skipped.

## Two collectors, two different trigger shapes

| Collector | `Name` | Reaches out itself? | What it reads from `parameters` | Fields it can populate |
|---|---|---|---|---|
| `SonarMetricCollector` | `sonar` | Yes — calls the configured SonarQube/SonarCloud `measures` Web API directly | optional `projectKey` override (defaults to the execution's `Context.Repository`) | `sonarIssues`, `securityFindings`, `codeSmells`, `coverage`, `duplication`, `cyclomaticComplexity` |
| `BuildResultCollector` | `build-result` | No — the daemon has no filesystem access to CI artifacts, same ADR-0005 §9 reason it has no access to a repo's working directory | `trx` and/or `cobertura` (raw XML **content**, not a path) | `buildSuccess`, `testSuccess` (from trx), `coverage` (from Cobertura `line-rate`) |

Both are reached through the same endpoint — `POST /executions/{id}/metrics/collect` with an optional `{"parameters": {...}}` body — because which collectors actually do anything is a function of what's registered and what's in `parameters`, not something the endpoint needs to know about. Pushing a trx file only causes `build-result` to produce a row; a Sonar-configured daemon calling the same endpoint with no parameters only causes `sonar` to produce one.

```
POST /executions/{id}/metrics/collect
{ "parameters": { "trx": "<TestRun ...>...</TestRun>" } }

GET /executions/{id}/metrics
→ [ { "id": "...", "collectedBy": "build-result", "buildSuccess": true, "testSuccess": true, ... } ]
```

JUnit XML support (mentioned alongside trx in the original phase scope) wasn't built this phase — trx covers this project's own CI, and a JUnit parser is a structurally identical follow-up (same "count total/failed testcases" shape) whenever it's actually needed.

## Configuration

Sonar needs a server URL and (usually) a token:

- `Plugins:sonar:BaseUrl` in daemon configuration (e.g. `appsettings.json`, or `Plugins__sonar__BaseUrl` as a container env var). Unset means the collector always returns `null` — no error, just nothing collected, since not every daemon has Sonar configured.
- The token is **not** read from configuration (ADR-0007: secrets never live in PromptOps config directly). `SonarMetricCollector` resolves it via `ISecretProvider.GetSecretAsync("sonar", "token")`, and the default `EnvironmentSecretProvider` reads it from the `PROMPTOPS_SECRET_SONAR_TOKEN` environment variable.

`BuildResultCollector` needs no configuration — everything it needs arrives in `parameters` per call.

## Plugin loading (ADR-0004, now real)

Both collectors ship as separate daemon-side plugin assemblies (`plugins/PromptOps.Plugins.Sonar`, `plugins/PromptOps.Plugins.BuildResult`), each implementing `IPromptOpsPlugin` and registering their `IMetricCollector` in `Register()`. `PromptOps.Host.Plugins.PluginLoader` scans `Plugins:Directory` (default: `plugins/` next to the daemon's own binaries — `/app/plugins` in the Docker image) for subdirectories, loads each one's primary DLL into its own isolated `AssemblyLoadContext`, and calls `Register`. See `docs/plugin-authoring.md` for the worked example and the type-identity pitfall that isolation loading has to get right.

Because which collectors run is entirely a function of DI registration, **adding a third collector plugin — or removing one — never touches `MetricsCollectionService`, the endpoints, or any other collector.** Drop a new plugin directory into `plugins/` (or remove one) and rebuild the image; that's the whole change. This is the direct mechanism behind Phase 5's acceptance criterion ("a config change to the daemon, not a code change").
