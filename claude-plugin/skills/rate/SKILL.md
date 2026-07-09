---
name: rate
description: Use when the user types "/promptops rate" or asks to rate, score, or evaluate the current PromptOps execution.
---

# /promptops rate

Human evaluation submission (`HumanEvaluation`: correctness, helpfulness, architecture, readability, completeness, hallucinations, confidence, overall satisfaction, notes) is Phase 6 of the PromptOps roadmap (`docs/project-plan.md`) and isn't built yet.

Tell the user plainly that this isn't implemented yet rather than fabricating a rating flow, and point them at `docs/project-plan.md` (Phase 6 — Human Evaluation) for what's planned. Do not attempt to call a daemon endpoint for this — none exists yet.
