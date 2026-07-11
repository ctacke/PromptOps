# Recommendation Engine v1 (Phase 9)

`Recommendation` is the payoff for every phase before this one: given what a developer is about to work on, surface which `PromptVersion` has actually performed best on similar work — anywhere on the machine, not just in the current repo.

## Classify-then-recommend

The developer supplies a task description, not tags — classification is internal, never a separate user-facing step (the phase's explicit design goal). `RecommendationService.RecommendForTaskAsync`:

```
taskDescription → IActivityClassifier.ClassifyAsync → tags[] → IRecommendationProvider.RecommendAsync → ranked Recommendation[]
```

### `IActivityClassifier` / `AIActivityClassifier`

Built on `IAIExecutionProvider`, the same "reuse the provider abstraction" pattern as `IAIEvaluationProvider` (Phase 7) — no separate AI dependency. `AIActivityClassifier` asks the configured backend to return a JSON array of lowercase, hyphenated tags (e.g. `["debugging", "csharp"]`) and reuses Phase 7's resilience machinery wholesale: `JsonExtraction.ExtractJsonValue` (refactored out of `AIJudgeEvaluationProvider` into a shared utility this phase, since it needed to extract arrays as well as objects) tolerates markdown fences and surrounding prose, and a malformed response gets retried — up to 3 attempts — with the parse error fed back into the prompt as a correction.

**Where it deliberately diverges from the judge**: if every attempt still fails, `AIActivityClassifier` returns an **empty list**, not an exception. A missing AI evaluation (Phase 7) is a data-integrity concern worth surfacing loudly; a failed classification just means "recommend broadly instead of narrowly" — and an empty tag list already means "no filter" to `IRecommendationProvider` (below), so this is a graceful degradation, not a broken pipeline.

### `IRecommendationProvider` / `TagAndHistoryRecommendationProvider`

```csharp
Task<IReadOnlyList<Recommendation>> RecommendAsync(IReadOnlyList<string> tags, string? repository = null, int limit = 5, CancellationToken cancellationToken = default);
```

1. Loads every prompt in the shared database as a lightweight candidate (`PromptRecommendationCandidate`: id, name, tags, and the specific version that would be recommended — the highest-numbered `Active` version, or the highest-numbered version of any status if none is `Active`). This is a **new** repository query (`IPromptRepository.GetRecommendationCandidatesAsync`) — nothing before this phase could list more than one prompt at a time. It never loads version `Content`, the same discipline `GetMetadataAsync` (Phase 2) established for a single prompt.
2. **Tag filter**: a candidate is excluded only if `tags` is non-empty and none of its tags match (case-insensitive). An empty `tags` list — including whatever `AIActivityClassifier` falls back to — means no filter, not "match nothing."
3. **Repository filter** (opt-in, `repository` parameter): excludes candidates with no execution history in that specific repo. Left `null`, results span every repo in the shared database — the default, and what makes a brand-new repo's session useful on day one (see the acceptance criteria below).
4. **Ranking**: candidates with a `PromptScore` (Phase 8, most recent by `ComputedAt`) rank above candidates with none — a prompt actually proven better beats an untested one regardless of tag-match count. Among scored candidates, higher `OverallScore` wins; tag-match count is the tie-breaker. Unscored candidates are still returned (there being no data yet isn't a reason to hide a tag match entirely — see Phase 8's "missing input ≠ scored zero" principle, docs/scoring.md), just always after every scored one.
5. Every result gets a **rationale**, not a bare number (the phase's explicit "not a black-box score" acceptance criterion) — e.g. *"Matched 2/2 requested tag(s). Score: 87.5/100 from 6 execution(s) across 3 repo(s)."* or *"Matched 1/2 requested tag(s). Not yet scored — no execution history recorded."*

Because tags live in `PromptMetadata` as a JSON-array text column (Phase 2's original schema — see `docs/prompt-repository.md`), matching happens in memory rather than as a SQL `WHERE`, consistent with this project's stated single-machine SQLite scale (the same "brute-force is fine here" reasoning `docs/architecture.md` uses for Phase 10's semantic search).

## `Recommendation`

```
Recommendation (query-time result — not persisted; a fresh set is computed on every call)
 ├─ queryContext            e.g. "tags=debugging,csharp; repository=my-repo"
 ├─ recommendedPromptVersionId
 ├─ rationale                human-readable, see above
 ├─ similarityScore          matched-tag fraction (0-1) — a placeholder for Phase 10's real semantic similarity
 └─ rank                     1-based position in this result set
```

## Surfaces

```
POST /recommendations
{ "taskDescription": "getting a NullReferenceException in the login flow", "repository": null, "limit": 5 }
→ 200 OK [ { "rank": 1, "recommendedPromptVersionId": "...", "rationale": "...", "similarityScore": 1.0, "queryContext": "..." }, ... ]
```

- MCP tool `recommend_prompt(taskDescription, repository?, limit?)` — an **instance** tool (`RecommendationTools`, DI-injected `RecommendationService`, same pattern as `HumanEvaluationTools` from Phase 6). ADR-0006 names "get a recommendation" as a canonical reason MCP tools exist alongside the ingestion API, so this is the primary way `/promptops recommend` reaches the daemon — no shelling out to curl. Deliberately still returns only `recommendedPromptVersionId` + `rationale`, never `Content` — a five-result list showing five full prompt bodies would be noisy, and most results past the top one are never actually read.
- `/promptops recommend` (`claude-plugin/skills/recommend/SKILL.md`): gathers a task description (asks, or infers from the ongoing conversation — never fabricates one), calls `recommend_prompt` with no `repository` filter by default, and presents the rationale alongside each result rather than just a ranked list of ids. For the **top-ranked result**, it now also calls `get_prompt_version_content` (`docs/statistics.md`) and shows the actual prompt text, not just its id and score — this is what makes a query like "show me the current prompt for creating a new feature" answerable in one command. Doesn't auto-select or apply a prompt version — presenting ranked options (with the top one's real content) is the deliverable; picking one is still the developer's call.

## Acceptance criteria, concretely

- **"Given tags/category, returns ranked prompt versions with a stated rationale (not a black-box score), drawing on history from any repo on the machine."** — `TagAndHistoryRecommendationProviderTests` seeds executions across multiple distinct `Context.Repository` values and asserts both the ranking order and the rationale text.
- **"A brand-new repo with zero history of its own still gets useful recommendations if a similar task has been run in another repo."** — proven directly: a candidate whose only execution history is in `"some-other-repo"` still surfaces when no `repository` filter is passed (the call a brand-new repo's session makes), and correctly disappears when a `repository` filter naming a repo it has no history in *is* passed.
- **"Given a free-text task description, `IActivityClassifier` returns tags that plausibly match its activity... without the developer declaring a category themselves."** — tested the same way Phase 7 tested judge output: a stub `IAIExecutionProvider` with canned responses, proving the classify-then-recommend wiring is correct. Actual semantic plausibility (does "NullReferenceException, stack trace" really mean "debugging"?) is the real AI backend's job once one exists, not something hard-coded here.
