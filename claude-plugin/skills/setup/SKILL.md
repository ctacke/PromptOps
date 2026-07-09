---
name: setup
description: Use when the user asks to "set up promptops", "install promptops", "start the promptops daemon", "promptops setup", or when a PromptOps SessionStart hook reported the daemon isn't reachable and the user wants it running.
---

# PromptOps daemon setup

The PromptOps daemon (ADR-0009) runs once per developer machine via Docker — not once per repo. This skill gets it running; it does not modify anything in the current repo.

## Steps

1. Check whether the daemon is already up: `GET http://127.0.0.1:5179/health` (e.g. via a short script, or `Invoke-RestMethod`/`curl` in Bash). If it responds `{"status":"ok",...}`, tell the user it's already running and stop here.
2. Confirm Docker is available: run `docker info`. If that fails, tell the user Docker Desktop (or an equivalent daemon) needs to be running first, and stop.
3. Try to start the daemon from a pre-built image:
   ```
   docker run -d --name promptops-daemon --restart unless-stopped -p 127.0.0.1:5179:8080 -v promptops-data:/data promptops-daemon:latest
   ```
   Always bind `127.0.0.1:5179`, never `0.0.0.0` or a bare port — the daemon must stay unreachable from outside this machine (ADR-0007).
4. If that fails because the image doesn't exist locally (`Unable to find image 'promptops-daemon:latest' locally`), there's no published registry image yet. Tell the user to clone the PromptOps source repo and build it there instead:
   ```
   git clone https://github.com/ctacke/PromptOps.git
   cd PromptOps
   docker compose up -d --build
   ```
   This builds and starts the same image `docker run` above expects, so this only needs to happen once per machine.
5. Poll `GET http://127.0.0.1:5179/health` for a few seconds until it responds, then confirm success to the user. If a session is already open in a repo with the PromptOps plugin installed, tool usage from this point on will be recorded once you start a new session (SessionStart already ran and found the daemon down).

Never run `docker run`/`docker compose` without telling the user what you're about to do first — this starts a real background service on their machine.
