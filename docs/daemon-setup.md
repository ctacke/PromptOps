# Daemon Setup (Phase 4a)

The daemon (ADR-0009) is a single Docker image, started once per developer machine — not once per repo. This doc covers that one-time setup. Installing PromptOps into an individual repo (the thin Claude Code plugin) is Phase 4b, covered separately in `docs/installing-promptops.md` once it lands.

## Starting the daemon

To run the daemon from the published package in GitHub Container Registry (GHCR):

```
docker run -d --name promptops-daemon --restart unless-stopped -p 127.0.0.1:5179:8080 -v promptops-data:/data ghcr.io/ctacke/promptops:latest
```

If you are developing/contributing to PromptOps and want to build the daemon from source instead:

```
docker compose up -d --build
```

This builds the image from the repo-root `Dockerfile` and starts it per `docker-compose.yml`.

Check it's up:

```
curl http://127.0.0.1:5179/health
```

(or open that URL in a browser — the response is a small JSON object, `{"status":"ok","pluginsLoaded":2}` — the `sonar` and `build-result` metric-collector plugins load by default, see below).

## Where data lives

The SQLite database and any future artifact storage live on a named Docker volume, `promptops-data`, mounted at `/data` inside the container. The volume — not the container — is what persists across restarts, rebuilds, and `docker compose down` (as long as you don't pass `-v`).

```
docker volume inspect promptops_promptops-data
```

To back up the database, copy the file out of the volume:

```
docker compose cp promptops-daemon:/data/promptops.db ./promptops-backup.db
```

## Network exposure

Per ADR-0007, the daemon must never be reachable from outside the host machine. `docker-compose.yml` publishes the container's port bound to `127.0.0.1` only (`127.0.0.1:5179:8080`) — not `0.0.0.0`. Do not change this binding without revisiting ADR-0007; a plain `"5179:8080"` mapping would expose the daemon to the whole network.

## Surfaces exposed

- **Loopback ingestion API** — `http://127.0.0.1:5179/executions/...` — what Claude Code hooks call (Phase 4b). See `docs/execution-tracking.md`, `docs/metrics.md`, and `docs/human-evaluation.md` for the endpoint contracts (execution tracking, engineering metrics, human evaluation).
- **MCP over HTTP** — `http://127.0.0.1:5179/mcp` (streamable HTTP transport). Registered automatically when the Claude Code plugin is installed (Phase 4b) — no manual `claude mcp add` step needed from inside a repo that has the plugin. Current tools: `health_check`, `version`, `create_prompt`, `create_prompt_version`, `get_prompt`, `list_prompts`, `activate_prompt_version`, `get_promotion_policy`, `update_promotion_policy`, `submit_human_evaluation`, `get_human_evaluations`, `run_ai_evaluation`, `get_ai_evaluation_policy`, `update_ai_evaluation_policy`, `recommend_prompt`.

## Metric-collector plugins

The image ships with two daemon-side plugins (ADR-0004) already built in: `sonar` and `build-result` (Phase 5, see `docs/metrics.md` and `docs/plugin-authoring.md`). Both load automatically — `pluginsLoaded` in `/health` reflects this — but `sonar` does nothing until it's configured, since not every daemon has a Sonar server to talk to. Set these on the host shell before `docker compose up` to point it at a real server:

```
$env:PROMPTOPS_SONAR_BASE_URL = "https://sonar.example.com"
$env:PROMPTOPS_SONAR_TOKEN = "..."
docker compose up -d
```

`docker-compose.yml` passes these through as `Plugins__sonar__BaseUrl` and `PROMPTOPS_SECRET_SONAR_TOKEN` — the token specifically goes through `ISecretProvider`, never plain configuration (ADR-0007). `build-result` needs no configuration; it only acts on content pushed to it per call.

## Upgrading the image

If you are running the pre-built GHCR image, pull the latest image and restart the container:

```powershell
docker pull ghcr.io/ctacke/promptops:latest
docker stop promptops-daemon
docker rm promptops-daemon
docker run -d --name promptops-daemon --restart unless-stopped -p 127.0.0.1:5179:8080 -v promptops-data:/data ghcr.io/ctacke/promptops:latest
```

If you built from source, pull the latest source, then rebuild and recreate the container:

```
git pull
docker compose up -d --build
```

The named volume is untouched by this — your data survives. Pending EF Core migrations run automatically at startup (`Program.cs` calls `Database.MigrateAsync()` before the app starts serving requests).

## Stopping the daemon

```
docker compose down
```

This stops and removes the container but leaves the `promptops-data` volume intact. To wipe stored data too (rarely what you want): `docker compose down -v`.

## Verifying the setup

`scripts/smoke-test.ps1` automates the checks above end-to-end: builds and starts the container, confirms the port is loopback-only, pushes a fixture execution through the ingestion API, confirms the MCP endpoint answers `tools/list` with `health_check` and `version`, restarts the container, and confirms the execution is still there afterward.

```
pwsh scripts/smoke-test.ps1
```
