---
name: recommend
description: Use when the user types "/promptops recommend" or asks PromptOps to recommend a prompt version for the current task.
---

# /promptops recommend

Prompt recommendation (classify a task description into activity tags via `IActivityClassifier`, then rank historical `PromptScore`s via `IRecommendationProvider` across every repo on the machine) is Phase 9 of the PromptOps roadmap (`docs/project-plan.md`) and isn't built yet.

Tell the user plainly that this isn't implemented yet rather than guessing at a recommendation, and point them at `docs/project-plan.md` (Phase 9 — Recommendation Engine v1) for what's planned. Do not attempt to call a daemon endpoint for this — none exists yet.
