# Execution Attribution & Proactive Recommendation (Phase 15)

Every phase up to now recorded executions, but the plugin opened each one against the all-zeros
`UNTRACKED_PROMPT_VERSION_ID` (`claude-plugin/hooks/lib/state.mjs`). All the objective signal
captured for free — diff churn, tool usage, and (via the metric collectors) build/test/Sonar
outcomes — therefore attached to *no* prompt version, so scoring and recommendation learned nothing
from the passively-captured majority of sessions. Architecture.md §5 (1) always intended the hook to
"resolve the PromptVersion to use … and open an ExecutionRecord"; Phase 15 finally does it, and
surfaces the resolved prompt in-session.

## Why attribution happens on the first prompt, not at SessionStart

`ExecutionRecord.PromptVersionId` is immutable once the record is created (`ExecutionRecord.cs` —
get-only, set in the private constructor, no update endpoint). Attribution needs the task
description, which doesn't exist yet at `SessionStart`. So the plugin now:

- **`SessionStart`** records only a *pre-open* state file `{ startedAtCommit, cwd, startedAtMs }` —
  capturing the diff baseline from the very start of the session — and does **not** open an
  execution.
- **`UserPromptSubmit`** (new hook), on the **first** turn only, sends the user's prompt to the
  daemon, which decides attribution and opens the (now correctly attributed) execution, then writes
  `executionId` back into the state file. Later turns are a no-op.
- **`PreToolUse`/`PostToolUse`** are unchanged except that `PostToolUse` no-ops until an
  `executionId` exists (i.e. between `SessionStart` and the first prompt).
- **`SessionEnd`** finalizes as before, and additionally clears a leftover pre-open file if a session
  ended before any prompt arrived.

Best-effort throughout: if the attributed-start call fails or times out, the hook falls back to
opening a plain untracked execution via `POST /executions/start`, so tool-usage/diff tracking still
works exactly as it did before Phase 15.

## The daemon decision — `ExecutionAttributionService`

`POST /executions/start-attributed` wraps `ExecutionAttributionService.StartAttributedAsync`
(`src/PromptOps.Application/Executions/`), which classifies the prompt via the existing
`IActivityClassifier` and takes one of three paths:

| Outcome | When | What happens |
|---|---|---|
| **untracked** | classifier returns no tags (not a development activity) | execution opened against `Guid.Empty`; no prompt created — non-dev chatter never pollutes the library |
| **recommended** | an existing prompt shares the task's **activity** | execution attributed to that prompt's version; its content is returned so the hook can surface it in-session |
| **captured** | a development task with no prompt for that activity | the developer's own prompt is captured as a new **Active** prompt named for the activity, and the execution is attributed to it |

The recommend-vs-capture decision keys off canonical **activity** tags
(`debugging`, `testing`, `code-authoring`, …) rather than raw semantic similarity, so "debug this"
captures a new debugging prompt even when a "create a feature" prompt already exists. Incidental
shared tags (e.g. a language like `csharp`) do not count toward the match. Interactive
`/promptops recommend` is unchanged and still uses the full semantic ranking
(`RecommendationService`) — that is a suggestion surface; this is an attribution decision, and they
legitimately want different behavior.

Natural dedup: because a captured prompt is Activated (and `GetRecommendationCandidatesAsync` prefers
the Active version), the *next* same-activity task matches it and attributes to it rather than
capturing a second prompt.

### Non-development gate

`AIActivityClassifier` is prompted to return an empty array `[]` for anything that isn't a
software-development activity. An empty tag list is the daemon's signal for "leave untracked," so a
question like "how are baseball stats derived?" is never captured as a prompt.

## Dependency

Classification requires a real `IAIExecutionProvider`. The default `ManualAIExecutionProvider`
returns nothing, so classification yields `[]` and every session is left untracked. For attribution
and proactive recommendation to do real work, configure the daemon with
`AIExecution:Provider=claude-cli` (see `docs/ai-evaluation.md`). A credential-free alternative —
delegating classification to the developer's local `claude` CLI from the hook (the ADR-0010 pattern
used by `auto-evaluate.mjs`) — is possible follow-on work, not built here.

## Testing

- `ExecutionAttributionEndpointsTests` (Host) exercises all three branches plus
  capture-then-recommend dedup over real HTTP, driving classification deterministically through
  `ManualAIExecutionProvider` (which echoes `parameters["output"]`).
- The hook lifecycle (pre-open → attributed open → idempotent later turns → finalize → pre-open
  cleanup) is verified against a locally-running daemon.
