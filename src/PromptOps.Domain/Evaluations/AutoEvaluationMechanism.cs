namespace PromptOps.Domain.Evaluations;

/// <summary>
/// Which mechanism runs the AI judge automatically when an execution finishes (Phase 13):
/// <see cref="Daemon"/> is <see cref="AIEvaluationPolicy"/>'s original behavior (Phase 8) —
/// <c>AutoAIEvaluationTrigger</c> calls a daemon-owned <c>IAIExecutionProvider</c> from a detached
/// background task. <see cref="ClientHook"/> instead delegates to the per-repo plugin's
/// <c>SessionEnd</c> hook, which shells out to the developer's own already-authenticated `claude`
/// CLI locally — no AI credentials live in the daemon or its container.
/// </summary>
public enum AutoEvaluationMechanism
{
    Daemon,
    ClientHook
}
