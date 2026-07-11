# Getting Started with PromptOps

Welcome! PromptOps is designed to help you track, score, and continuously improve the prompts you use with AI coding assistants. It runs silently in the background of your development sessions, recording which prompt styles lead to clean builds, passing tests, and high-quality refactorings, so you always know what phrasing works best.

This guide will get you up and running in a few simple steps.

---

## Prerequisites

- **Docker Desktop** (or an equivalent Docker daemon) running on your machine.
- **Claude Code** installed and running in your target project.

> [!NOTE]
> The PromptOps daemon runs **once per machine**, not per repository. If you or a teammate have already started the daemon on this machine, you can skip straight to [Step 2: Install the plugin](#2-install-the-plugin-into-your-project).

---

## 1. Start the Daemon

The daemon acts as your local database and scoring engine. You can spin it up with a single Docker command from any terminal:

```bash
docker run -d --name promptops-daemon --restart unless-stopped -p 127.0.0.1:5179:8080 -v promptops-data:/data ghcr.io/ctacke/promptops:latest
```

*Prefer a hands-off setup?* If you've already checked out your project, you can just start Claude Code and type `/promptops setup` to let the assistant pull and run the container for you.

Once running, you can access the visual telemetry dashboard by opening `http://127.0.0.1:5179` in your web browser.

---

## 2. Install the Plugin into Your Project

Navigate to your project's repository, start a Claude Code session, and run:

```bash
claude plugin marketplace add ctacke/PromptOps
claude plugin install promptops@promptops
```

This registers the session hooks, registers the daemon as an MCP server, and adds the `/promptops` commands to your environment. No configuration files to edit!

---

## 3. Seed Your Starter Prompts

PromptOps works best when it has a catalog of prompts to track work against. Run the initialization command in your Claude session:

```
/promptops init
```

This seeds the shared database with eight starter prompts covering everyday tasks (fixing bugs, writing tests, refactoring, code reviews, and performance tuning).

---

## 4. Manage Prompts with Natural Language

**You never need to make raw API calls, curl requests, or write scripts to manage your prompts.** Since Claude has access to the PromptOps MCP tools, you can manage your prompts using natural language. 

Try asking Claude:
> *"Create a prompt named 'Refactor' with the tag `refactor` and content: 'Analyze this module and refactor it for readability while ensuring all existing unit tests pass.'"*

Claude will create the prompt, tag it, and write the initial draft version behind the scenes.

---

## 5. Write Code as You Normally Do

Once the plugin is installed, you don't need to change how you work. Just code, run tests, and end your Claude session normally. In the background:
- **SessionStart**: Automatically opens a new execution record.
- **Telemetry**: Tracks tool usage, durations, and changes.
- **SessionEnd**: Measures files changed, builds completed, and saves the session data.

---

## 6. Rate and Evaluate Your Sessions

To help PromptOps learn what works, you can evaluate your sessions:

*   **Rate your experience**: Type `/promptops rate`. Claude will ask you a few quick satisfaction questions (1-5 rating on correctness, readability, helpfulness, etc.) and save the score.
*   **Get an AI review**: Type `/promptops evaluate`. Claude will analyze the changes made during the session against your project's guidelines and verify that requirements were met.
*   **Automate it**: Want AI evaluations on every session? Just tell Claude:
    > *"Turn on automatic AI evaluation."*

---

## 7. Get Smart Recommendations

When you start a new task, ask Claude for the best prompt to use:

```
/promptops recommend I need to fix a memory leak in the database query runner.
```

PromptOps will look at your task description, classify it, search your prompt history (across all projects on your machine), and show you the best-performing prompt version along with the rationale.

---

## 8. Let Successful Prompts Promote Themselves

By default, new versions start in `Draft`. You can let successful versions automatically promote themselves to `Active` by setting a policy. Just ask Claude:

> *"Set the auto-promotion policy threshold to 85."*

Now, any prompt version that achieves a score above 85 based on build success, test metrics, and evaluations will automatically become the active version for recommendations.

---

## Upgrading an Existing Installation

Upgrades are simple and split into two independent parts: upgrading the shared daemon on your machine, and updating the plugin in your specific project repositories. **Your data is safe:** all prompts, version histories, execution logs, and configurations survive updates.

### 1. Upgrade the Daemon (Once per machine)

If you are running the pre-built GHCR image, pull the latest version and recreate the container:

```bash
docker pull ghcr.io/ctacke/promptops:latest
docker stop promptops-daemon
docker rm promptops-daemon
docker run -d --name promptops-daemon --restart unless-stopped -p 127.0.0.1:5179:8080 -v promptops-data:/data ghcr.io/ctacke/promptops:latest
```

*Want to take a quick backup first just in case?* You can copy your SQLite database out of the volume with a single command:
```bash
docker cp promptops-daemon:/data/promptops.db ./promptops-backup.db
```

If you built from source instead of running the GHCR image, just pull the latest commits and rebuild:
```bash
git pull
docker compose up -d --build
```

### 2. Upgrade the Plugin (Per repository)

To update the session hooks and commands in a specific project, open a Claude Code session in that repository and run:

```
claude plugin marketplace update promptops
```

*Tip:* After upgrading, run `/reload-plugins` to load the updated hooks and commands into your active session.

---

## Where to Go Next

*   [daemon-setup.md](file:///F:/repos/ctacke/PromptOps/docs/daemon-setup.md) — Advanced setup details, backing up your database, and connecting Sonar or CI plugins.
*   [installing-promptops.md](file:///F:/repos/ctacke/PromptOps/docs/installing-promptops.md) — Deep dive into the plugin hooks and commands.
*   [architecture.md](file:///F:/repos/ctacke/PromptOps/docs/architecture.md) — Learn how PromptOps is designed to keep your data local and private.
