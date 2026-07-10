# Semantic Search / Knowledge Base (Phase 10)

Phase 9's recommendation engine only ever matched on exact tags. Phase 10 gives it a second signal — semantic similarity between the new task and each `PromptVersion`'s own embedded text — so a task that's *conceptually* related but never got classified into a matching tag can still surface.

## What gets embedded, and against what

There's no standalone archive of past task descriptions anywhere in the domain model, so "semantically similar past tasks" is reframed as: **the new task description, compared against each `PromptVersion`'s own embedded text.** That text already exists — it's just never been indexed before:

```
BuildEmbeddingText(prompt, version) = prompt.Name + prompt.Metadata.Description + prompt.Metadata.Tags + version.Content
```

`PromptService` keeps this index up to date on the two events that change what that text represents:

- `CreateVersionAsync` — indexes the new version immediately after it's persisted.
- `TagPromptAsync` — tags are part of the embedded text, so a re-tag re-indexes the *latest* version only (versions before the latest one keep whatever embedding they already had; only the latest is ever realistically recommended — see `PromptRecommendationCandidate`'s "best version" selection, `docs/recommendations.md`).

## `IEmbeddingProvider` / `HashingBagOfWordsEmbeddingProvider`

```csharp
Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default);
```

Deliberately **not** built on `IAIExecutionProvider` — unlike `IAIEvaluationProvider` (Phase 7) and `IActivityClassifier` (Phase 9), text embedding is a different model shape than chat/completion, so reusing that abstraction would leak a mismatched contract rather than save one.

`HashingBagOfWordsEmbeddingProvider` is the reference implementation — same "prove the pipeline now, real backend is a future plugin" story as `ManualAIExecutionProvider` (Phase 3). It's a deterministic, local, 128-dimension hashing-trick bag-of-words:

1. Lowercase, tokenize on non-alphanumeric boundaries.
2. Hash each token into one of 128 buckets via **FNV-1a** — not `string.GetHashCode()`, which .NET randomizes per process as a security mitigation and would silently break embedding reproducibility across daemon restarts (`Is_Deterministic_Across_Separate_Provider_Instances` tests this directly by using two separate provider instances, simulating two process runs).
3. L2-normalize the resulting vector, so cosine similarity reduces to a plain dot product. Empty text yields the zero vector, not a divide-by-zero.

This is a bag-of-words model — it captures shared vocabulary, not meaning (`"debugging a null reference"` and `"a null pointer crash"` share zero tokens and won't score as similar). That's an accepted limitation of the reference implementation, not a bug; a real embedding-model plugin is the intended upgrade path.

## `IEmbeddingStore` / `EmbeddingStore`

```csharp
Task StoreAsync(Guid subjectId, string subjectType, float[] embedding, CancellationToken cancellationToken = default);
Task<IReadOnlyList<EmbeddingMatch>> FindSimilarAsync(float[] queryEmbedding, string subjectType, int limit, CancellationToken cancellationToken = default);
```

- `StoreAsync` upserts by `(subjectId, subjectType)` — re-indexing (e.g. after a re-tag) replaces the stored vector rather than duplicating it.
- `FindSimilarAsync` is **brute-force cosine similarity**: loads every row of the given `subjectType` into memory and ranks in C#. No `sqlite-vec`, no dedicated vector database — the same "single-machine, single-database scale makes brute-force entirely viable" reasoning `docs/architecture.md` already applies to Phase 9's in-memory tag matching. `subjectType` is the partition key for the scan (today, only `EmbeddingSubjectTypes.PromptVersion` exists, but the schema doesn't assume it's the only one that ever will).
- Vectors persist as a JSON-array text column (`FloatListValueConverter`), the same JSON-in-a-column pattern every other non-primitive column in this project uses (`StringListValueConverter`, `StringDictionaryValueConverter`, ...).

## `SemanticRecommendationProvider` (v2)

`IRecommendationProvider` gained an optional `taskDescription` parameter:

```csharp
Task<IReadOnlyList<Recommendation>> RecommendAsync(IReadOnlyList<string> tags, string? taskDescription = null, string? repository = null, int limit = 5, CancellationToken cancellationToken = default);
```

`SemanticRecommendationProvider` is now the bound `IRecommendationProvider` (v1, `TagAndHistoryRecommendationProvider`, stays registered as a concrete type — see "Why v1 stays alongside v2" below). It blends three components:

| Component | Weight | Source |
|---|---|---|
| Semantic similarity | 0.4 | Cosine similarity between the query embedding and the candidate's stored embedding |
| Tag match | 0.3 | Matched-tag fraction, same as v1 |
| Historical score | 0.3 | Most recent `PromptScore.OverallScore`, same as v1 |

**A missing component is excluded from both the numerator and the denominator of the weighted average — never treated as a zero.** This is the same principle `WeightedSumScoringProvider` established in Phase 8 (`docs/scoring.md`), applied here for the first time to a *ranking* blend rather than a scoring formula. Concretely: a candidate with no stored embedding yet contributes nothing from the semantic component and is scored purely on whatever components it does have — it is not penalized as "confirmed dissimilar." (Implementation note: this is why `BlendScore` is fed an explicit `double?` computed via `TryGetValue`, not `Dictionary.GetValueOrDefault`, which would silently return `0.0` — a real, present-but-zero value — for a missing key.)

The query text embedded per call is `taskDescription` if given, else the requested `tags` joined with spaces; if neither is present, the embedding call is skipped entirely and every candidate's semantic component is simply absent.

### Why v1 stays alongside v2, and why the tag filter had to move

v1's tag filter (exclude any candidate that matches none of the requested tags) is *load-bearing for v1's own correctness*, but is exactly the behavior Phase 10's acceptance criterion requires v2 to **not** have ("semantically similar past tasks surface even without exact tag overlap"). Wrapping v2 around v1 as a decorator would apply v1's exclusion before semantic scoring ever got a chance to run, defeating the phase outright.

Instead, the parts both providers agree on were extracted into `RecommendationCandidateGatherer` (internal to `PromptOps.Infrastructure`): it applies the **repository** filter (shared, both providers exclude candidates with no execution history in a requested repo) but *not* the tag filter. Each provider then makes its own downstream call on tags — v1 excludes on zero overlap, v2 folds tag-match into the blend and never excludes on it alone. v1 remains registered as a concrete type (not bound to `IRecommendationProvider`) rather than deleted, since nothing in the phase called for retiring it and a future config toggle (see `docs/project-plan.md`'s Phase 11+ notes) may want to select between them.

## `Recommendation`, updated rationale

`similarityScore` is now the real semantic cosine similarity (0-1) where available, rather than Phase 9's matched-tag-fraction placeholder. Rationale text now states the semantic contribution explicitly, e.g. *"Semantic similarity: 0.82. Matched 0/1 requested tag(s). Score: 87.5/100 from 6 execution(s) across 3 repo(s)."* — or *"No semantic signal available (not yet indexed)."* when the candidate has no stored embedding, keeping the phase 9 "not a black-box score" principle intact.

## Acceptance criteria, concretely

- **"Semantically similar past tasks surface even without exact tag overlap, including across repos."** — proven at both layers: `SemanticRecommendationProviderTests` (fixture embeddings, pure unit test) seeds two candidates whose tags match neither requested tag and asserts the semantically similar one still outranks the unrelated one; `RecommendationEndpointsTests.A_Semantically_Similar_Task_Ranks_Above_An_Unrelated_One_Despite_Neither_Matching_Tags` proves the same thing end-to-end through `/recommendations` using the real `HashingBagOfWordsEmbeddingProvider`.
- **"Missing data isn't scored zero."** — `A_Missing_Component_Is_Excluded_From_The_Blend_Not_Scored_As_Zero` uses a pair of numbers chosen specifically to flip ranking order between a correct (exclude-missing) and a naive (missing = 0) implementation, so the test would fail under either regression.
