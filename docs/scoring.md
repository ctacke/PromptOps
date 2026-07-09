# Scoring Engine (Phase 8)

`PromptScore` is what everything from Phases 5-7 was collected *for* — a single (per config version) number summarizing how well a `PromptVersion` has performed, computed from every execution of it, across every repo on the machine (ADR-0005: `PromptVersion` is not repo-scoped).

## Domain model

```
ScoringConfig (immutable, named + versioned)
 ├─ id, name, version, createdAt
 └─ weights: { humanRating, sonar, tests, build, acceptanceCriteria, manualFixes, reviewComments, regressionBugs }

PromptScore (independent aggregate, 0..n per PromptVersion — not per execution)
 ├─ id, promptVersionId, scoringConfigId, computedAt
 ├─ overallScore           (0-100)
 ├─ componentScores         (json breakdown, one entry per component that had data)
 └─ sampleSize              (how many Finished executions contributed)
```

Two things distinguish `PromptScore` from every other aggregate built so far: it's keyed to a **`PromptVersionId`**, not an `ExecutionId` — it aggregates *across* every execution of that version — and its `ScoringConfigId` points at an **immutable** config. "Changing the weights" always means creating a new `ScoringConfig` version, never mutating one in place. That immutability is the entire reproducibility guarantee (the phase's second acceptance criterion): a score computed under config version 3 keeps meaning exactly what it meant even after version 4 exists, because nothing about version 3 can change out from under it.

Like every aggregate since Phase 5, both id fields are plain values, not foreign keys, and `PromptScore` is additive — every recompute (on-demand or debounced) produces a new row, so score trends over time stay observable rather than being overwritten.

## The formula

For a given `promptVersionId` and `ScoringConfig`, `WeightedSumScoringProvider`:

1. Loads every **Finished** execution of that prompt version (`sampleSize` = this count; `InProgress` executions are excluded — they have no output to judge yet).
2. Gathers every `EngineeringMetrics`/`HumanEvaluation`/`AIEvaluation` row attached to those executions.
3. Reduces each of the eight named inputs to a single 0-100 component score, **averaged across every execution that has data for it**:

| Component | Derived from | Formula |
|---|---|---|
| `humanRating` | `HumanEvaluation.OverallSatisfaction` (1-5) | `(rating - 1) / 4 * 100` |
| `acceptanceCriteria` | `AIEvaluation.SatisfiesAcceptanceCriteria` (bool?) | 100 if true, 0 if false, per evaluation |
| `sonar` | `EngineeringMetrics.Coverage` where `CollectedBy == "sonar"` | used as-is (already 0-100) |
| `tests` | `EngineeringMetrics.TestSuccess` (bool?) | 100 if true, 0 if false, per row |
| `build` | `EngineeringMetrics.BuildSuccess` (bool?) | 100 if true, 0 if false, per row |
| `manualFixes` | `EngineeringMetrics.ManualEdits` (int?) | `max(0, 100 - count * 10)` |
| `reviewComments` | `EngineeringMetrics.ReviewComments` (int?) | `max(0, 100 - count * 5)` |
| `regressionBugs` | `EngineeringMetrics.RollbackNeeded` (bool?) | 100 if false (no rollback), 0 if true |

   `sonar`'s single-number choice (just coverage, not a composite of issues/smells/duplication/complexity) and the `manualFixes`/`reviewComments` decay constants (10 and 5 points per unit) are a deliberately simple starting point, not an empirically-tuned formula — richer per-metric weighting is a natural future refinement once there's real data to calibrate against. Today, nothing populates `ManualEdits`/`ReviewComments`/`RollbackNeeded` at all (a gap documented back in Phase 5), so in practice `manualFixes`/`reviewComments`/`regressionBugs` are the phase's real, live "missing input" case, not just a hypothetical one exercised only in tests.

4. Combines the components that matter into `overallScore`:

   ```
   overallScore = Σ(componentScore × weight) / Σ(weight)
                  — summed only over components where weight > 0 AND data exists
   ```

   **This is the entire answer to "zero-weight and missing-input edge cases" (the phase's explicit testing requirement).** A component is excluded from both the numerator and the denominator if either its weight is 0 (explicit or defaulted — a `ScoringConfig` that never set `sonar` gets `Sonar = 0`) or no data exists for it across all of the prompt version's executions. Both cases behave identically: excluded, not scored as a 0. This is what keeps a config that doesn't care about Sonar, and a project that's never run Sonar, from arriving at different-but-equally-wrong answers — neither drags the score down for a reason that has nothing to do with quality. If **no** component has both a positive weight and data (a brand-new prompt version, or a config with every weight at 0), `overallScore` is defined as `0.0` — there's nothing to average.

   `componentScores` (the persisted breakdown) reports every component that had data, **regardless of its weight** — it's a full picture of what's known, not just what the config chose to act on.

## Default config

The first time any code asks for a `ScoringConfig` named `"default"` and none exists, `ScoringService` creates version 1 with these weights (so a fresh daemon works with zero setup):

```
humanRating: 0.30    acceptanceCriteria: 0.20    sonar: 0.15    tests: 0.15
build: 0.10          manualFixes: 0.05           reviewComments: 0.025    regressionBugs: 0.025
```

Human rating and AC-satisfaction weighted heaviest as the most direct "did this work" signals available today; the three currently-unpopulated fields (`manualFixes`/`reviewComments`/`regressionBugs`) weighted lightly on purpose. These are a reasonable starting point, not a tuned formula — change them by creating a new config version (below).

## Recompute-on-event (debounced) + on-demand

Two ways a score gets (re)computed, both landing on the same `ScoringService.RecomputeAsync`:

- **On-demand**: `POST /prompts/{promptVersionId}/scores` — synchronous, runs immediately, returns the new `PromptScore`.
- **Debounced, on-event**: `ScoreRecomputeTrigger` (an `IDomainEventHandler` registered against `ExecutionRecorded`, `MetricsCollected`, `HumanEvaluationSubmitted`, and `AIEvaluationRecorded`) calls `IScoreRecomputeScheduler.RequestRecompute(promptVersionId)` whenever any of those fire. The default `DebouncedScoreRecomputeScheduler` collapses rapid-fire requests for the same prompt version into a single recompute 10 seconds after the *last* one — a burst of `PostToolUse`-driven metrics or several evaluations landing within seconds of each other triggers one recompute, not one per event. It's a singleton holding per-prompt-version timers; when a timer fires, it opens a fresh DI scope (it can't hold a scoped `ScoringService` itself) and calls the same `RecomputeAsync` the on-demand endpoint uses. This has no HTTP surface of its own — it just happens inside the daemon process.

## Ingestion API

```
POST /scoring-configs
{ "name": "default", "weights": { "humanRating": 0.4, "sonar": 0.1, ... } }
→ 200 OK { "id": "...", "name": "default", "version": 2, "weights": {...}, "createdAt": "..." }
   (version auto-increments per name — omit it, the server computes "latest + 1")

GET /scoring-configs?name=default
→ 200 OK [ { version: 1, ... }, { version: 2, ... } ]   (every version, chronological)

POST /prompts/{promptVersionId}/scores
{ "scoringConfigId": "...", "scoringConfigName": "..." }   (both optional — omit both for "default", latest version)
→ 200 OK { "overallScore": 82.5, "componentScores": {...}, "sampleSize": 6, "scoringConfigId": "..." }
→ 404 if an explicit scoringConfigId doesn't exist

GET /prompts/{promptVersionId}/scores
→ 200 OK [ { ... }, { ... } ]   (full history, chronological, may be empty)
```

## Testing

- `ScoringConfigTests`/`PromptScoreTests` (Domain) — construction validation (negative weights, version < 1, negative sample size), `ScoreComputed` raised only on `Compute` (not `Rehydrate`).
- `WeightedSumScoringProviderTests` (Infrastructure, pure unit tests against in-memory fakes — no SQLite) — the phase's explicit edge cases: a zero-weighted component with data present is excluded from the average; a positively-weighted component with no data is excluded (not scored as 0); a prompt version with zero data yields `0.0` without throwing; `InProgress` executions are excluded from `sampleSize`; changing weights across three configs against the same underlying data produces three different, exactly-predictable scores (reproducibility); the manual-fixes/review-comments decay formula.
- `ScoringEndpointsTests` (Host) — full HTTP round trip against the real production DI graph: config version auto-increment, recompute-with-no-data, recompute-after-real-execution-and-evaluation-data producing the expected deterministic score, 404 on an unknown config id.
