# Getting Started

You have an existing project and want PromptOps tracking your Claude Code sessions in it, scoring the results, and recommending a better starting prompt next time. This is the shortest path from zero to that, in order — laid out as the loop you'll actually live in day to day, not just a feature checklist.

If you want the full picture of what's actually happening at each step, `docs/promptops-flow.html` is a visual walkthrough; this doc is the practical "do this" version.

## Prerequisites

- Docker Desktop (or an equivalent Docker daemon) running.
- Claude Code, with the target project open as a repo.
- The PromptOps daemon runs **once per machine**, not once per repo — if a teammate already started it, skip straight to [Install the plugin](#install-the-plugin).

## 1. One-time setup, from a console

### Start the daemon

Run the daemon from the published package in GitHub Container Registry (GHCR):

```powershell
docker run -d --name promptops-daemon --restart unless-stopped -p 127.0.0.1:5179:8080 -v promptops-data:/data ghcr.io/ctacke/promptops:latest
```

Confirm it's up:

```powershell
Invoke-RestMethod http://127.0.0.1:5179/health
# {"status":"ok","pluginsLoaded":2}
```

The daemon binds to `127.0.0.1` only — it's never reachable from outside this machine (ADR-0007). Its SQLite database lives in a named Docker volume (`promptops-data`) that survives restarts. Full details, backup/restore, and configuring the `sonar`/`build-result` metric plugins: `docs/daemon-setup.md`.

If you'd rather have Claude Code do this step for you inside a session, `/promptops setup` walks through the same thing.

### Install the plugin

From inside your **existing project's repo**, in a Claude Code session:

```
claude plugin marketplace add ctacke/PromptOps
claude plugin install promptops@promptops
```

Same mechanism used to install `context-mode`. This registers the four session hooks (`SessionStart`/`PreToolUse`/`PostToolUse`/`SessionEnd`), the daemon as an MCP server (`claude-plugin/.mcp.json` already points at `http://127.0.0.1:5179/mcp` — nothing further to configure), and the `/promptops` skills. Full details: `docs/installing-promptops.md`.

## 2. Start Claude Code in your repo

Just `claude`, as normal — nothing else changes about how you work. The `SessionStart` hook fires transparently: it checks the daemon's health, gathers repo/branch/commit/developer-id/detected-languages *locally* (the daemon has no filesystem access to your repo), and opens an `ExecutionRecord` for the session. If the daemon isn't reachable, you'll see a note in context that tracking is disabled for this session — everything else still works, nothing is lost.

## 3. `/promptops init` — seed your first prompts

PromptOps needs at least one `Prompt`/`PromptVersion` to track work against. There's no auto-import of prompts you've been keeping elsewhere — you create them once, then reuse and refine them through everything below.

The fastest path is:

> `/promptops init`

This seeds the shared database with eight starter prompts covering common developer activities (fix a bug, add a feature, write tests, refactor, document, review a PR, investigate a performance issue, security review) — each created *and* activated (not left in Draft), already tagged so `/promptops recommend` can match against them from day one. It's safe to run more than once, from any repo: it checks names first and only creates what's missing, so running it again from a second project won't duplicate the catalog in the shared database.

For anything not covered by the starter set, ask Claude to create one in plain language — it has `create_prompt`/`create_prompt_version` MCP tools available once the plugin is installed — or call the REST API directly:

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

A new version always starts as `Draft`. It doesn't need to be `Active` for execution tracking or scoring to work — only for it to be the one recommendations and auto-promotion treat as "the current one" (step 10). Repeat this for each distinct kind of task you want PromptOps to learn from separately — you don't need to front-load all of them; add more as you notice a recurring task that doesn't have one yet.

### 3a. Optional: connect engineering metrics

There's no daemon-side pull integration for CI or Sonar — it's push-based:

- **Sonar**: set `PROMPTOPS_SONAR_BASE_URL`/`PROMPTOPS_SONAR_TOKEN` before starting the daemon (or restart it after setting them) — the daemon then queries the SonarQube/SonarCloud measures API itself, per execution's repository.
- **CI (build/test results)**: no config needed, but your CI job has to push the raw content itself: `POST /executions/{id}/metrics/collect` with `{"parameters": {"trx": "...", "cobertura": "..."}}` after a build finishes.

See `docs/metrics.md` for the exact commands and payload shapes.

### 3b. Optional: trust the AI judge right away with `/promptops evaluate`

Running this before you've even made a change (or right after your first one) walks the client-delegated flow — this replaces MCP's now-deprecated `sampling` capability (ADR-0010). The daemon's `prepare_ai_evaluation` hands back a judge prompt and a correlation id *without* calling any model itself; Claude Code answers that prompt itself, using its own current reasoning — reviewing the session's task honestly, not rubber-stamping its own output; then `submit_ai_evaluation_result` records the verdict, or asks for one corrected retry (up to 3 attempts) if the answer didn't match the required schema. No separate AI backend, no extra credentials — the daemon never touches a model in this flow.

## 4. Do some Claude Code development. While you work, the plugin will:

- Time every tool call: `PreToolUse` writes a start timestamp locally (no network call, so it stays fast); `PostToolUse` pairs it with the tool name and duration and posts one tool-usage record to your session's `ExecutionRecord` on the daemon.
- Do all of this best-effort and silently — a daemon hiccup here never surfaces as an error, since the tool call itself already succeeded regardless.

## 5. Ending a session — `/exit`, not `/clear`

**Only `/exit` (or otherwise closing the session) finalizes the execution.** `/clear` clears your context but does **not** end the session in the hook-lifecycle sense — it isn't one of the four events this plugin listens for, so it does *not* trigger the finish step below. If you `/clear` mid-session expecting a clean boundary, the same `ExecutionRecord` just keeps accumulating tool usage until you actually exit.

When the session really ends, `SessionEnd` fires: it computes a `git diff --numstat` against the commit recorded at `SessionStart` (files changed, lines added/removed) and posts `executionTimeMs`/`aiProviderId: "claude-code"`/those diff stats to `/executions/{id}/finish`, marking the record `Finished`. Best-effort with a short timeout — it can't block session termination, so on a daemon hiccup here the record is simply left unfinished rather than the session hanging.

Verify it worked:

```powershell
Invoke-RestMethod http://127.0.0.1:5179/executions/<execution-id>
```

should show `"status":"Finished"` with non-empty `filesChanged` after you've edited something and exited.

## 6. Whenever you want to review — run `/promptops rate`

Do this after any session, or on demand — you'll want to do it more often while the database is still new (thin history means noisier recommendations later). It:

- Finds your current session's execution id automatically.
- Asks eight quick ratings (via `AskUserQuestion`, not free text): correctness, helpfulness, architecture, readability, completeness, hallucinations (yes/no), your own confidence, and overall satisfaction — plus optional notes.
- Submits a `HumanEvaluation` (immutable, additive — rating the same execution twice adds a second row, never overwrites the first).
- That submission is one of the three triggers (rating / engineering metrics / AI evaluation) that automatically recomputes the prompt version's score — you don't run anything else for that to happen.

## 7. Back to step 4 — keep developing, keep rating.

## 8. After some ratings build up, ask for a recommendation with `/promptops recommend`

There's no fixed threshold — do this more often while the database is new, since recommendations sharpen as more scored executions accumulate; even a handful is enough to get tag-matched results. Give it a plain task description, not tags:

> `/promptops recommend` — I need to fix a null reference exception in the login flow.

It:

- Classifies your task description into activity tags internally (no separate step for you), then ranks every `PromptVersion` on the machine — not just this repo, unless you ask it to filter to one — blending tag match and historical score. Scored candidates always outrank unscored ones; among scored, higher score wins, tag-match count is the tiebreaker.
- Presents each candidate's `rationale` in plain language (e.g. "Matched 2/2 requested tags. Score 87.5/100 from 6 executions across 3 repos.") alongside the recommended version id — never just a bare score.
- **Never applies anything automatically** — picking it up and actually using its content for your next prompt is your call, always.

## 9. Back to step 4 again.

## 10. Once you're confident, go hands-off

You don't have to go all the way to full automation to stop hand-activating versions — a single
manual step covers that on its own, whenever you decide a version is ready:

```powershell
Invoke-RestMethod "http://127.0.0.1:5179/prompts/$promptId/versions/$versionId/activate" -Method Post
```

For the rest of the loop, two independent policy toggles get you fully hands-off — turning both on removes `/promptops rate` and `/promptops evaluate` from the loop entirely:

**Automatic AI evaluation:**

```powershell
Invoke-RestMethod http://127.0.0.1:5179/ai-evaluation-policy -Method Put -ContentType "application/json" -Body '{"autoEvaluateOnFinish":true}'
```

(or ask `/promptops evaluate` to flip it). The daemon then runs the judge itself the moment every session finishes, in the background, no `/promptops evaluate` needed. It's off by default since each evaluation is a real judge-model call.

**Caveat, today:** this requires the daemon to have a real AI backend configured (`AIExecution:Provider`) — out of the box it's bound to a test stub with nothing to answer the judge prompt, so turning this on without also configuring a real provider fails silently in the background (logged, never surfaced) rather than actually judging anything. A design for doing this via the client-side session-end hook instead — reusing your own already-logged-in `claude` CLI, no daemon credentials at all — exists (`docs/ai-evaluation.md`, "Phase 13") but isn't built yet.

**Automatic prompt promotion:**

```powershell
Invoke-RestMethod http://127.0.0.1:5179/promotion-policy -Method Put -ContentType "application/json" -Body (@{
    requireHumanEvaluation    = $false
    autoPromotionEnabled      = $true
    minimumScoreThreshold     = 85.0
    minimumMarginOverActive   = $null
} | ConvertTo-Json)
```

Any `PromptVersion` whose recomputed score clears `minimumScoreThreshold` (or beats the currently-active version by `minimumMarginOverActive`) gets activated automatically — no manual `.../activate` call needed. `requireHumanEvaluation` must be `false` for auto-promotion to be allowed at all — that's the explicit trade you're making by turning this on. Full details, including why the two fields are linked: `docs/promotion-policy.md`.

With both toggles set: sessions run, executions finish, the judge evaluates automatically, scores recompute, and winning versions promote themselves — the loop above (steps 4–9) keeps happening without you doing anything beyond writing code.

## Updating an existing installation

Setup in step 1 was two independent parts — updating follows the same split, and doing one doesn't update the other. Neither one touches your data: prompts, versions, executions, evaluations, and scores all survive an update either way.

### Update the daemon

Still once per machine. If you're running the published GHCR image:

```powershell
docker pull ghcr.io/ctacke/promptops:latest
docker stop promptops-daemon
docker rm promptops-daemon
docker run -d --name promptops-daemon --restart unless-stopped -p 127.0.0.1:5179:8080 -v promptops-data:/data ghcr.io/ctacke/promptops:latest
```

`docker stop`/`docker rm` only remove the container — your data lives in the separate `promptops-data` named volume (`-v promptops-data:/data`), which isn't touched as long as you don't add `-v` to a later `docker compose down`. Re-mounting the same volume on the new container is what makes this safe: prompts, versions, executions, and evaluations are all still there once it comes back up, and any pending EF Core migrations run automatically at startup, before the daemon starts serving requests.

If you built from source instead: `git pull`, then `docker compose up -d --build` — same volume, same guarantee.

If you'd rather not take that on faith, back it up first — it's one command:

```powershell
docker compose cp promptops-daemon:/data/promptops.db ./promptops-backup.db
```

Full details: `docs/daemon-setup.md` ("Upgrading the image", "Where data lives").

### Update the plugin

Per repo, since the plugin is installed per repo. From inside a Claude Code session in that repo:

```
claude plugin marketplace update promptops
```

(or the slash-command equivalent, `/plugin marketplace update promptops`, or `/plugin` → **Marketplaces** tab → **Update marketplace listing**). This pulls the latest commit from `ctacke/PromptOps` — refreshed hooks, skills, and MCP registration. Plugins don't auto-update on their own, so this is something you re-run when you want the latest, not a one-time step. It only refreshes the marketplace listing — run `/reload-plugins` afterward so the current session actually picks up the change.

## Where things stand

- Execution tracking, engineering metrics, human evaluation, AI evaluation, scoring, semantic recommendation, and manual/automatic promotion are all live daemon-side (see `README.md`'s Status line for the current phase list).
- There's still no bulk import for prompts you're keeping somewhere else (a wiki, a prompts folder, teammates' heads) — creating them one at a time via step 3 is the only path today.
- `docs/architecture.md` and `docs/project-plan.md` cover the full design and what's still unscheduled roadmap versus shipped.
