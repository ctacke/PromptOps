namespace PromptOps.Application.Evaluations;

/// <summary>
/// Tracks judge prompts awaiting an answer from a delegating MCP client (ADR-0010/Phase 12), keyed
/// by a server-generated, single-use correlation id. Deliberately not required to be durable —
/// a delegated evaluation is meant to complete within one live conversation turn, so the default
/// implementation (<c>InMemoryPendingDelegatedEvaluationStore</c>) doesn't survive a daemon restart.
/// </summary>
public interface IPendingDelegatedEvaluationStore
{
    /// <summary>Records a new pending prompt and returns its correlation id.</summary>
    Guid Create(Guid executionId, string prompt);

    /// <summary>
    /// Looks up a pending prompt. Returns <c>false</c> if the correlation id is unknown or has
    /// expired (an expired entry is evicted as a side effect of the lookup).
    /// </summary>
    bool TryGet(Guid correlationId, out PendingDelegatedEvaluation? pending);

    /// <summary>Replaces the prompt/attempt count after a failed answer, refreshing its expiry.</summary>
    void Update(Guid correlationId, string prompt, int attempt);

    /// <summary>Removes a pending prompt — called once it's answered successfully or exhausted.</summary>
    void Remove(Guid correlationId);
}
