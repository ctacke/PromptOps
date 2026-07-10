---
name: init
description: Use when the user types "/promptops init" or asks to seed/initialize PromptOps with starter prompts.
---

# /promptops init

Seeds the shared PromptOps database with a curated catalog of common developer-task prompts, using the daemon's `list_prompts`/`create_prompt`/`create_prompt_version`/`activate_prompt_version` MCP tools (registered by this plugin's `.mcp.json` — call them directly, don't shell out to curl for this).

## 1. Confirm the daemon is reachable

Call `list_prompts`. If it fails to connect, tell the user the daemon isn't running and suggest `/promptops setup` first — don't try to start it yourself here, that's `setup`'s job.

## 2. Read the starter catalog

Read `starter-prompts.json`, in this skill's own directory (next to this `SKILL.md`). It's a JSON array of `{ name, description, tags, content }` entries — read it as data, don't paraphrase or rewrite the `content` field, it's meant to be used verbatim as the prompt text.

## 3. Skip anything that already exists

The `list_prompts` result from step 1 gives you every existing prompt's name. For each catalog entry whose `name` matches an existing prompt (case-insensitive), skip it — don't create a duplicate. This also means if the user already created their own prompt with a colliding name (e.g. by following `docs/getting-started.md`'s own "Fix a bug" example), `init` treats that as already covering that slot.

## 4. Create what's missing

For each catalog entry that doesn't already exist:

1. Call `create_prompt` with `name`, `description`, and `tags` from the entry.
2. Call `create_prompt_version` with the returned prompt's id, `content` from the entry, and `createdBy` set to the developer's git email (`git config user.email` in the current repo — same pattern `/promptops rate` uses for `evaluatorId`; fall back to asking the user if git isn't configured).
3. Call `activate_prompt_version` with the prompt id and the new version's id — seeded prompts come in ready to use, not sitting in `Draft` waiting for someone to promote them.

## 5. Report a plain summary

Tell the user how many prompts were created and how many were skipped because they already existed, by name. Don't editorialize about the content of the catalog.
