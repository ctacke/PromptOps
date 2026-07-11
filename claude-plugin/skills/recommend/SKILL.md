---
name: recommend
description: Use when the user types "/promptops recommend" or asks PromptOps to recommend a prompt version for the current task.
---

# /promptops recommend

Gets a ranked prompt recommendation via the daemon's `recommend_prompt` MCP tool (registered by this plugin's `.mcp.json` — look for a tool whose name contains both `promptops` and `recommend_prompt` among your available tools; call it directly, don't shell out to curl for this).

## 1. Get a task description, not tags

Classification into activity tags happens inside the daemon (`IActivityClassifier`) — you supply a free-text description of what the developer is trying to do, never tags directly. If the user typed `/promptops recommend` with no further detail, ask them what they're working on (or infer it from the current conversation if it's already obvious — e.g. they were just discussing a specific bug or feature). Don't invent a task description from nothing.

## 2. Call `recommend_prompt`

Pass the task description as-is. Leave `repository` unset by default — that's what surfaces recommendations from other repos on the machine when this repo has no history of its own (the whole point of the shared daemon, per `docs/architecture.md` §9). Only pass a `repository` filter if the user explicitly asks to restrict results to prompts already proven in this specific repo.

## 3. Show the actual content, not just a rationale

`recommend_prompt` itself only returns a `recommendedPromptVersionId` and a `rationale` — never the prompt's actual text. For the **top-ranked result**, call `get_prompt_version_content` with its `recommendedPromptVersionId` and show the returned `content` directly, so the answer to "what's the current prompt for X" is the actual prompt text, not just an id and a score. Show the `rationale` alongside it (tag match + score + sample size) — this is deliberate: a black-box ranking isn't useful to a developer deciding whether to trust it. For any additional (non-top) results, the rationale + id is enough; don't fetch content for every one unless the user asks to see more than the top match.

If the tool returns an empty list, say so plainly — don't fabricate a recommendation. That means nothing in the shared database matches the classified tags yet, which is expected on a genuinely new kind of task.

## What this doesn't do

`/promptops recommend` doesn't automatically select or apply a prompt version — presenting ranked options with rationale is the deliverable; picking one and actually using it is still the developer's call.
