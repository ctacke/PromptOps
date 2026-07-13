# Automatic Prompt Refinement (Phase 16)

The AI judge (Phase 7) produces `AIEvaluation.SuggestedPromptImprovements` on every evaluation, but
until now nothing consumed them — a human had to read a suggestion and hand-author the next version.
`AutoPromotionTrigger` (Phase 11) has always been ready to activate a better Draft, but nothing ever
drafted one. Phase 16 closes that loop. It ships in three sub-phases:

- **16a — draft the improvement** *(complete)*: when an evaluation with suggestions is recorded for
  the active version of a prompt, draft a new Draft version incorporating them.
- **16b — synthetic-benchmark pre-screen** *(complete)*: before a draft is allowed near real work,
  run it and the active version against generated inputs; deprecate any draft that regresses.
- **16c — A/B shadow adoption** *(complete)*: give benchmark-passing drafts a controlled slice of real
  traffic so they earn a real `PromptScore`, then let the existing `AutoPromotionTrigger` promote on
  real evidence — closing the fully-autonomous loop.

This document covers 16a, 16b, and 16c.

## `RefinementPolicy` — the on/off switch

A single global, mutable settings singleton (`src/PromptOps.Domain/Refinement/RefinementPolicy.cs`),
same pattern as `PromotionPolicy` and `AIEvaluationPolicy`. Phase 16a defines one field:

```
RefinementPolicy
 ├─ AutoRefinementEnabled   bool,   default false — 16a gate; today's behavior unchanged until opted in
 ├─ SyntheticSampleSize     int,    default 0     — 16b: scenarios to benchmark against (0 = benchmarking off)
 ├─ MinQualityDelta         double, default 0     — 16b: margin the draft must beat the active version by
 ├─ AbExplorationRate       double, default 0     — 16c: fraction of sessions routed to an eligible draft (0 = off)
 └─ UpdatedAt
```

Managed via `GET/PUT /refinement-policy` and the `get_refinement_policy` / `update_refinement_policy`
MCP tools. `RefinementPolicyService.GetOrCreateDefaultAsync` lazy-bootstraps the default row, so a
fresh daemon behaves exactly as before.

## `PromptRefinementTrigger` → `PromptRefinementService`

`PromptRefinementTrigger` (`src/PromptOps.Infrastructure/Refinement/`) is a second
`IDomainEventHandler<AIEvaluationRecorded>` alongside `ScoreRecomputeTrigger`. It mirrors
`AutoAIEvaluationTrigger` exactly: a fast synchronous policy check (so it never blocks the
event-publish chain that fires from `AIEvaluationService`/`DelegatedAIEvaluationService`), then the
real work — which includes an LLM call to rewrite the prompt — in a detached background task with its
own DI scope and `CancellationToken.None`, wrapped in a log-and-swallow catch.

The work itself lives in `PromptRefinementService.RefineFromEvaluationAsync` (an ordinary,
unit-testable application service). Its guards, in order:

| Guard | Outcome |
|---|---|
| evaluation not found / no `SuggestedPromptImprovements` | `NoEvaluation` / `NoSuggestions` |
| execution not found | `ExecutionNotFound` |
| execution attributed to the all-zeros id | `Untracked` — *this is why Phase 15 is a prerequisite* |
| attributed version isn't the **Active** one | `VersionNotActive` — only the live prompt is refined |
| a Draft already exists for the prompt | `CandidateAlreadyExists` — anti-runaway: one candidate in flight |
| refiner returns blank / unchanged text | `NoContentChange` |
| otherwise | `Drafted` — a new Draft version is created |

On the happy path it calls `PromptService.CreateVersionAsync` (new version is `Draft`, parentage
auto-set to the active version, embedding indexed — so the draft is semantically searchable for 16c),
stamped `createdBy = "promptops-refinement"`.

## `IPromptRefinementProvider`

The rewrite is delegated to `IPromptRefinementProvider`, whose reference implementation
`AIPromptRefinementProvider` is built on `IAIExecutionProvider` — the same "reuse the execution
provider, no new AI dependency" pattern as `IAIEvaluationProvider` and `IActivityClassifier`. It asks
the model to rewrite the prompt so following it would address every suggestion while preserving the
prompt's intent and template variables, and cleans the output (trims, strips a stray markdown fence).
A blank response degrades to "no draft" rather than throwing.

## Dependency

Like classification (Phase 15) and AI evaluation, both refinement and benchmarking need a real
`IAIExecutionProvider`. With the default `ManualAIExecutionProvider` the refiner produces no content
(`NoContentChange`, no draft) and the benchmark generates no scenarios (`Inconclusive`, draft left
pending) — configure `AIExecution:Provider=claude-cli` for them to do real work.

## Phase 16b — the synthetic-benchmark pre-screen

A drafted refinement must prove it doesn't regress against generated inputs *before* it is ever
allowed near real developer work. Chained inside the same detached task, right after a successful
draft: `PromptRefinementTrigger` calls `PromptBenchmarkService.BenchmarkCandidateAsync`.

**`IPromptBenchmarkProvider` / `AIPromptBenchmarkProvider`** (built on `IAIExecutionProvider`):
generates `SyntheticSampleSize` synthetic task scenarios, runs both the active and candidate prompt
on each, and grades the two outputs together (0-100 each) so the scores are directly comparable.
Returns the per-version averages, or `null` when nothing usable could be produced (e.g. the no-op
`ManualAIExecutionProvider` generates no scenarios) — treated as **inconclusive**, never as a failure.

**`PromptBenchmarkService.BenchmarkCandidateAsync`** — the gate, recording a `RefinementCandidate`
(persisted, so 16c can reliably find A/B-eligible drafts and the scores survive for observability):

| Condition | Candidate status | Draft version |
|---|---|---|
| `SyntheticSampleSize == 0` | `PendingBenchmark` (benchmarking disabled) | left Draft (manual review) |
| benchmark `null` (inconclusive) | `PendingBenchmark` | left Draft — never deprecated on no evidence |
| `candidateScore ≥ activeScore + MinQualityDelta` | `AbEligible` | left Draft — eligible for 16c shadow traffic |
| otherwise (regressed / didn't clear margin) | `Rejected` | **Deprecated** — never reaches real work |

`RefinementCandidate` (`src/PromptOps.Domain/Refinement/`) carries the lineage
(prompt / draft / active-baseline version ids), the benchmark scores, and its status. It is persisted
in its own `RefinementCandidates` table rather than inferring eligibility from `PromptVersion.Status`
alone, so a benchmark-passing auto-refinement is distinguishable from an arbitrary human-authored
Draft.

## Phase 16c — A/B shadow adoption (closing the loop)

An `AbEligible` draft still isn't *recommended* (`GetRecommendationCandidatesAsync` prefers the active
version, and the interactive `/promptops recommend` surface should keep showing the proven prompt).
Instead, 16c routes a controlled fraction of real sessions to it at **attribution time** — the point
where a session's `PromptVersionId` is actually assigned and therefore where an execution can earn a
score.

**`AbVersionSelector`** (`src/PromptOps.Application/Refinement/`), applied in
`ExecutionAttributionService`'s recommend branch: with probability `AbExplorationRate`, and only when
the matched prompt has an `AbEligible` `RefinementCandidate`, it returns the draft's version id
instead of the active one. The ε coin flip is isolated behind `IExplorationSampler`
(`RandomExplorationSampler` in Infrastructure; deterministic in tests) so the randomness stays out of
the logic. The session transparently runs the draft prompt, and its execution is attributed to the
draft.

From there **nothing new is needed**: the draft's execution flows through the exact same pipeline as
any other — `ScoreRecomputeTrigger` recomputes a `PromptScore` for the draft version, `ScoreComputed`
fires, and the existing `AutoPromotionTrigger` (Phase 11) promotes the draft once it clears the
promotion policy's threshold/margin over the active version. When it's promoted (Draft → Active, old
active → Deprecated), future A/B lookups key on the *new* active version and no longer find the
candidate, so shadow routing naturally stops.

Fully autonomous adoption therefore requires three opt-ins working together, each off by default:
`RefinementPolicy` (`AutoRefinementEnabled` + `SyntheticSampleSize`/`MinQualityDelta` +
`AbExplorationRate`) to draft, screen, and shadow-test; and `PromotionPolicy`
(`AutoPromotionEnabled`, `RequireHumanEvaluation=false`, a threshold/margin) to promote on the
resulting evidence. A team that wants a human in the loop leaves auto-promotion off and activates
`AbEligible` drafts manually via `activate_prompt_version`.

## Testing

- `RefinementPolicyTests` / `RefinementCandidateTests` (Domain) — policy defaults/toggle/validation
  and candidate transition rules.
- `PromptRefinementServiceTests` (Infrastructure) — every 16a guard + the happy path, against fakes.
- `PromptBenchmarkServiceTests` (Infrastructure) — the 16b gate: eligible / rejected+deprecated /
  didn't-clear-margin / benchmarking-disabled / inconclusive.
- `PromptRefinementTriggerTests` (Infrastructure) — the policy gate + detached delegation + the
  draft→benchmark chain + failure isolation, mirroring `AutoAIEvaluationTriggerTests`.
- `AbVersionSelectorTests` (Infrastructure) — the 16c ε-greedy routing: off / declined / explores to
  an eligible draft / explores but none eligible.
- `AIPromptRefinementProviderTests` / `AIPromptBenchmarkProviderTests` (Infrastructure) — prompt
  construction, output cleaning, and generate→run→grade→average, against scripted execution stubs.
- `RefinementPolicyEndpointsTests` (Host) — the endpoints (incl. the 16b/16c fields and out-of-range
  rejection) over real HTTP against the real DI graph (which also exercises the `AddRefinementPolicy`,
  `AddSyntheticBenchmark`, and `AddAbExplorationRate` migrations on a fresh database).
- `ExecutionAttributionEndpointsTests` (Host) — with `AbExplorationRate = 1.0` a session is routed to
  the eligible draft; with the default (0) it uses the active version.
