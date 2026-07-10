# Optional Human Evaluation / Automatic Prompt Promotion (Phase 11)

Every prior phase assumed a human eventually decides which `PromptVersion` is "the" one to use — but nothing in the codebase ever actually enforced or acted on that decision. `PromptVersion.Activate()`/`Deprecate()` existed since Phase 1 and were never called; there was no `/prompts` REST or MCP surface at all. Phase 11 closes both gaps: a manual way to promote a version, and an optional automatic one driven by `PromptScore`.

## `Prompt.ActivateVersion` — the "exactly one Active version" invariant

```csharp
prompt.ActivateVersion(versionId);
```

The aggregate root enforces this itself (same precedent as `Prompt.Rehydrate` already enforcing "no duplicate version numbers"): the target version must be `Draft` (or already `Active`, in which case the call is a no-op — idempotent, not an error); whichever sibling version currently holds `Active` gets `Deprecate()`d first. Reactivating a `Deprecated` version (a rollback) is deliberately **not** supported — `PromptVersion.Activate()` only allows `Draft → Active`, and the aggregate validates the target's legality *before* touching any sibling, so a rejected activation never leaves the prompt with zero Active versions.

Both the manual and automatic paths funnel through this exact method, so the invariant holds identically either way.

## `PromotionPolicy` — a global settings singleton, not a versioned config

Unlike `ScoringConfig` (Phase 8), which is immutable and versioned because a past `PromptScore` must keep meaning what it meant when computed, `PromotionPolicy` is a single mutable row — an operational on/off switch, closer to a feature flag than a scoring methodology. There's nothing to reproduce; there's only "what's the policy right now."

```
PromotionPolicy
 ├─ RequireHumanEvaluation    bool, default true — today's unchanged default behavior
 ├─ AutoPromotionEnabled      bool, default false
 ├─ MinimumScoreThreshold     double? (0-100) — an absolute bar
 ├─ MinimumMarginOverActive   double? (>=0) — how much a candidate must beat the active version's score by
 └─ UpdatedAt
```

Validation (`PromotionPolicy.Update`):
- `AutoPromotionEnabled` requires `RequireHumanEvaluation == false` — auto-promotion **is** what replaces the human sign-off step, so the two settings can't both be "on."
- `AutoPromotionEnabled` requires at least one of `MinimumScoreThreshold`/`MinimumMarginOverActive` to be set.
- Threshold/margin combine with **OR** semantics — clearing either one alone is sufficient to promote.

`PromotionPolicyService.GetOrCreateDefaultAsync` lazy-bootstraps the default row on first access, same pattern as `ScoringService.ResolveConfigAsync` for `ScoringConfig` — a fresh daemon needs no manual setup step and behaves exactly as it did before this phase until someone opts in.

## `AutoPromotionTrigger`

Reacts to `ScoreComputed` (Phase 8) — the first-ever registered handler for that event; nothing consumed it before this phase.

```
ScoreComputed → policy.AutoPromotionEnabled? → load owning Prompt → still Draft? → clears threshold or margin? → Prompt.ActivateVersion
```

- If the policy doesn't exist yet or `AutoPromotionEnabled` is off, no-op.
- If the scored version is already `Active`, or is `Deprecated`, no-op — never resurrects a deliberately retired version.
- The margin check compares against the *currently active* version's most recent `PromptScore` (if any). With no active version yet, only the absolute threshold can trigger promotion.
- On promotion, calls `Prompt.ActivateVersion` and logs the decision — no new domain event; `Prompt` raises none today (`CreateVersion`/`AddTags`/`Rename` are all silent), so this stays consistent with that existing precedent rather than introducing event-raising nothing yet consumes.

`ScoreComputed` only carries `PromptVersionId`, not `PromptId` — `IPromptRepository.GetByVersionIdAsync` (new this phase) loads the owning aggregate from just the version id, same "new repository method per new need" precedent as `GetRecommendationCandidatesAsync`/`GetMetadataAsync`.

## Surfaces

```
POST /prompts/{promptId}/versions/{versionId}/activate   → manual promotion (200 OK, idempotent; 404 if prompt/version unknown; 409 if the target is Deprecated)
GET  /promotion-policy                                    → current policy (lazily bootstraps the default)
PUT  /promotion-policy                                     → update policy (400 on validation failure)
```

MCP tools (`PromotionTools`, instance/DI-injected, same pattern as `HumanEvaluationTools`/`RecommendationTools`): `activate_prompt_version`, `get_promotion_policy`, `update_promotion_policy`.

**Known gap, unchanged by this phase**: there is still no REST/MCP surface for *creating* a Prompt/PromptVersion — only activating an already-existing one. Every recent phase's live smoke test has hit this same limitation; closing it is future work, not required for Phase 11's acceptance criteria.

## Acceptance criteria, concretely

- **"A config toggle lets a team skip human evaluation entirely."** `PromotionPolicy.RequireHumanEvaluation` — `PromotionPolicyEndpointsTests` prove the default (`true`, unchanged behavior) and that it can be turned off.
- **"When a PromptScore clears a threshold, or beats the active version by a margin, the daemon automatically activates it — no human sign-off required."** `AutoPromotionTriggerTests` cover both conditions independently (absolute threshold with no active version yet; margin over an existing active version's score), that clearing neither is a no-op, that an already-active version is a no-op, and that a `Deprecated` version is never resurrected.
- **"Missing data isn't scored zero"-style discipline extends to policy validation, not just scoring**: enabling auto-promotion without human-eval off, or without any threshold/margin configured, is rejected outright (`PromotionPolicyTests`) rather than silently doing nothing.
