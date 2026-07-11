# Inspecting PromptOps: statistics and prompt content

Two related but separate needs: a bird's-eye view of what's in the shared database, and being able to actually read a prompt's text rather than just its id.

## `GET /statistics` / `get_statistics`

A single snapshot, composed from five independent queries run concurrently (`StatisticsService`, `src/PromptOps.Application/Statistics/StatisticsService.cs`) — every field is computed in SQL (`COUNT`/`GROUP BY`/`AVERAGE`), never by loading full aggregates into memory, the same discipline `GetMetadataAsync`/`GetRecommendationCandidatesAsync`/`GetAllNamesAsync` already established for prompts specifically.

```json
{
  "prompts": { "promptCount": 12, "versionCount": 31, "versionCountByStatus": { "Draft": 9, "Active": 12, "Deprecated": 10 } },
  "executions": { "totalCount": 214, "countByStatus": { "InProgress": 2, "Finished": 212 }, "countByRepository": { "github.com/ctacke/PromptOps": 180, "github.com/ctacke/OtherRepo": 34 } },
  "scores": { "count": 187, "averageOverallScore": 78.4 },
  "humanEvaluationCount": 92,
  "aiEvaluationCount": 45
}
```

`scores.averageOverallScore` is `null`, not `0`, when no scores have ever been computed — the same "missing data isn't scored zero" principle `WeightedSumScoringProvider` follows for its own component weighting (`docs/scoring.md`).

Reachable over REST (`GET /statistics`) or MCP (`get_statistics`, no arguments).

## Reading a prompt version's actual content

Nothing returned a `PromptVersion`'s `Content` except at the moment it was created — `GET /prompts/{id}` / `get_prompt` are metadata-only (name/description/tags) by design, and `recommend_prompt` only ever returned an id + rationale. That made "show me the current prompt for X" unanswerable end-to-end without a second, previously-nonexistent lookup step.

```
GET /prompt-versions/{versionId}   → { promptId, promptName, versionId, versionNumber, content, status, tags }
```

`get_prompt_version_content(promptVersionId)` is the MCP equivalent. Both reuse `IPromptRepository.GetByVersionIdAsync` (`PromptService.GetVersionDetailAsync`) — the same lookup Phase 11's `AutoPromotionTrigger` already needed, extended here to be reachable from outside the daemon for the first time.

### `/promptops recommend` now shows content, not just an id

`recommend_prompt` itself is unchanged — still returns `recommendedPromptVersionId` + `rationale`, deliberately lean (a recommendation list showing five full prompt bodies would be noisy, and most results after the top one are never actually read). The `/promptops recommend` skill now follows up automatically: for the **top-ranked result only**, it calls `get_prompt_version_content` and shows the actual text alongside the rationale. This is what makes "show me the current promptops prompt for creating a new feature" answerable in one command: classify → rank → fetch content → show it.

## Testing

- `StatisticsIntegrationTests` (Infrastructure) — one throwaway SQLite file per test (not the shared-per-class `SqliteFixture` pattern — these are global counts, so a row from one test leaking into another's assertion would be a real bug, same reasoning `PromotionPolicyRepositoryTests` already established), covering every repository's new stats/count query.
- `PromptServiceVersionDetailTests` (Infrastructure, fakes) — content/status/tags round-trip, reflects activation, `null` for an unknown version.
- `StatisticsEndpointsTests` / `PromptEndpointsTests` (Host) — full HTTP round trip against the real production DI graph.
