# Daemon Setup (Phase 4a)

The daemon (ADR-0009) is a single Docker image, started once per developer machine — not once per repo. This doc covers that one-time setup. Installing PromptOps into an individual repo (the thin Claude Code plugin) is Phase 4b, covered separately in `docs/installing-promptops.md` once it lands.

## Starting the daemon

```
docker compose up -d --build
```

This builds the image from the repo-root `Dockerfile` and starts it per `docker-compose.yml`. First build pulls the .NET 10 SDK/ASP.NET base images; subsequent builds reuse Docker's layer cache and are fast unless a `.csproj` changed.

Check it's up:

```
curl http://127.0.0.1:5179/health
```

(or open that URL in a browser — the response is a small JSON object, `{"status":"ok","pluginsLoaded":0}`).

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

- **Loopback ingestion API** — `http://127.0.0.1:5179/executions/...` — what Claude Code hooks call (Phase 4b). See `docs/execution-tracking.md` for the endpoint contract.
- **MCP over HTTP** — `http://127.0.0.1:5179/mcp` (streamable HTTP transport). Register it as a remote MCP server in Claude Code:

  ```
  claude mcp add --transport http promptops http://127.0.0.1:5179/mcp
  ```

  Today it exposes two tools — `health_check` and `version` — enough to prove connectivity. Real tools (search history, get a recommendation, submit a rating) land as the use cases behind them are built in later phases.

## Upgrading the image

Pull the latest source, then rebuild and recreate the container. The named volume is untouched by this — your data survives:

```
git pull
docker compose up -d --build
```

Pending EF Core migrations run automatically at startup (`Program.cs` calls `Database.MigrateAsync()` before the app starts serving requests).

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
