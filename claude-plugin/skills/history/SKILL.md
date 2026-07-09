---
name: history
description: Use when the user types "/promptops history" or asks to see past PromptOps executions, scores, or evaluations for a prompt.
---

# /promptops history

Querying execution/score/evaluation history is assembled from Phases 3 (execution tracking, done), 5 (engineering metrics), 6 (human evaluation), 7 (AI evaluation), and 8 (scoring) of the PromptOps roadmap (`docs/project-plan.md`). Only execution tracking exists today, and it isn't yet exposed as a query surface — the ingestion API (`docs/execution-tracking.md`) only supports starting/updating/finishing a single execution by id, not listing or filtering.

Tell the user plainly that a browsable history view isn't implemented yet rather than fabricating results, and point them at `docs/project-plan.md` for what's planned. Do not attempt to call a daemon endpoint for this — none exists yet.
