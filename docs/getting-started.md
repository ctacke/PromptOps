# Getting Started

You have an existing project and want PromptOps tracking your Claude Code sessions in it, scoring the results, and recommending a better starting prompt next time. This is the shortest path from zero to that, in order.

If you want the full picture of what's actually happening at each step, `docs/promptops-flow.html` is a visual walkthrough; this doc is the practical "do this" version.

## Prerequisites

- Docker Desktop (or an equivalent Docker daemon) running.
- Claude Code, with the target project open as a repo.
- The PromptOps daemon runs **once per machine**, not once per repo — if a teammate already started it, skip straight to [Install the plugin into your project](#2-install-the-plugin-into-your-project).

## 1. Start the daemon

Run the daemon from the published package in GitHub Container Registry (GHCR):

```powershell
docker run -d --name promptops-daemon --restart unless-stopped -p 127.0.0.1:5179:8080 -v promptops-data:/data ghcr.io/ctacke/promptops:latest
```

Confirm it's up:

```powershell
Invoke-RestMethod http://127.0.0.1:5179/health
# {"status":"ok","pluginsLoaded":2}
```

The daemon binds to `127.0.0.1` only — it's never reachable from outside this machine (ADR-0007). Its SQLite database lives in a named Docker volume (`promptops-data`) that survives restarts. Full details, backup/restore, and configuring the `sonar`/`build-result` metric plugins: [daemon-setup.md](file:///F:/repos/ctacke/PromptOps/docs/daemon-setup.md).

If you'd rather have Claude Code do this step for you inside a session, `/promptops setup` walks through the same thing.

## 2. Install the plugin into your project

From inside your **existing project's repo**, in a Claude Code session:

```
claude plugin marketplace add ctacke/PromptOps
claude plugin install promptops@promptops
```

This registers the session hooks, the daemon as an MCP server, and the `/promptops` skills — no further config editing needed. Full details: `docs/installing-promptops.md`.

## 3. Add your first prompt

PromptOps needs at least one `Prompt`/`PromptVersion` to track work against. There's no auto-import of prompts you've been keeping elsewhere — you create them once, then reuse and refine them through everything below.

The fastest path is:

> `/promptops init`

This seeds the shared database with eight starter prompts covering common developer activities (fix a bug, add a feature, write tests, refactor, document, review a PR, investigate a performance issue, security review) — already tagged so `/promptops recommend` can match against them from day one. It's safe to run more than once, from any repo: it checks names first and only creates what's missing, so running it again from a second project won't duplicate the catalog in the shared database.

For anything not covered by the starter set, ask Claude to create one — it has `create_prompt` and `create_prompt_version` MCP tools available once the plugin is installed:

> "Create a PromptOps prompt called 'Fix a bug' with content 'Investigate the reported bug, identify the root cause, and fix it with a minimal, targeted change.' and tag it `bugfix`."

Or call the REST API directly:

```powershell
$prompt = Invoke-RestMethod http://127.0.0.1:5179/prompts -Method Post -ContentType "application/json" -Body (@{
    name     = "Fix a bug"
    metadata = @{ description = "Standard bug-fix prompt"; tags = @("bugfix") }
} | ConvertTo-Json)

Invoke-RestMethod "http://127.0.0.1:5179/prompts/$($prompt.id)/versions" -Method Post -ContentType "application/json" -Body (@{
    content   = "Investigate the reported bug, identify the root cause, and fix it with a minimal, targeted change."
    createdBy = $env:USERNAME
} | ConvertTo-Json)
```

A new version always starts as `Draft`. It doesn't need to be `Active` for execution tracking or scoring to work — only for it to be the one recommendations and auto-promotion treat as "the current one" (step 6).

Repeat this for each distinct kind of task you want PromptOps to learn from separately — "fix a bug," "add a test," "write a migration," whatever categories make sense for your team. You don't need to front-load all of them; add more as you notice a recurring task that doesn't have one yet.

## 4. Just use Claude Code normally

Nothing else changes about how you work. Start a session, do the task, end the session. In the background:

- `SessionStart` opens an `ExecutionRecord` for the session.
- Every tool call gets logged (name, count, duration).
- `SessionEnd` computes what actually changed (files/lines) and closes out the record.

Verify it's working:

```powershell
Invoke-RestMethod http://127.0.0.1:5179/executions/<execution-id>
```

should show `"status":"Finished"` with non-empty `filesChanged` after you've edited something and ended the session.

## 5. Rate the result — optional

Once a session's execution has finished, ask Claude:

> `/promptops rate`

It'll ask you a handful of 1-5 questions (correctness, helpfulness, architecture quality, readability, completeness, hallucinations, confidence, overall satisfaction) and submit them as a `HumanEvaluation`. Scoring recomputes automatically whenever new data lands for an execution — a rating, metrics arriving, or an AI evaluation — so you don't need to trigger anything else afterward.

If you also want the AI-judge pass (`docs/ai-evaluation.md`) — a second AI opinion checking the diff against acceptance criteria and ADRs — ask Claude:

> `/promptops evaluate`

Or turn it on for every execution automatically, no manual step needed:

> `/promptops evaluate` — turn on automatic AI evaluation

(equivalently, `Invoke-RestMethod http://127.0.0.1:5179/ai-evaluation-policy -Method Put -ContentType "application/json" -Body '{"autoEvaluateOnFinish":true}'`). It's off by default since each evaluation is a real judge-model call.

Engineering metrics (build/test/Sonar) arrive on their own once CI runs, if those plugins are configured — see `docs/daemon-setup.md`.

## 6. Get a recommendation next time

Starting a new task, ask Claude:

> `/promptops recommend` — I need to fix a null reference exception in the login flow.

The daemon classifies your task description into activity tags, then ranks every prompt version on the machine — blending semantic similarity, tag match, and historical score (`docs/knowledge-base.md`, `docs/recommendations.md`) — and returns ranked results with a stated rationale, not just a score. This spans every repo tracked on this machine, not just the current one, so a prompt proven in one project surfaces for similar work in another.

It doesn't apply anything automatically — picking a recommended `promptVersionId` and actually using its content for your next prompt is still your call.

## 7. Let good versions promote themselves — optional

By default, nothing ever marks a version `Active` on its own — you decide when a `PromptVersion` is the one to treat as current:

```powershell
Invoke-RestMethod "http://127.0.0.1:5179/prompts/$promptId/versions/$versionId/activate" -Method Post
```

If you'd rather the daemon do this automatically once a version proves itself, turn on the promotion policy:

```powershell
Invoke-RestMethod http://127.0.0.1:5179/promotion-policy -Method Put -ContentType "application/json" -Body (@{
    requireHumanEvaluation    = $false
    autoPromotionEnabled      = $true
    minimumScoreThreshold     = 85.0
    minimumMarginOverActive   = $null
} | ConvertTo-Json)
```

From then on, any version whose computed score clears the threshold (or beats the currently active version's score by a configured margin) gets activated with no `/promptops rate` and no manual step. Full details, including why `requireHumanEvaluation` and `autoPromotionEnabled` are linked: `docs/promotion-policy.md`.

## Where things stand

- Execution tracking, engineering metrics, human evaluation, AI evaluation, scoring, semantic recommendation, and manual/automatic promotion are all live daemon-side (see `README.md`'s Status line for the current phase list).
- There's still no bulk import for prompts you're keeping somewhere else (a wiki, a prompts folder, teammates' heads) — creating them one at a time via step 3 is the only path today.
- `docs/architecture.md` and `docs/project-plan.md` cover the full design and what's still unscheduled roadmap versus shipped.
