using System.Collections.Concurrent;
using PromptOps.Application.Evaluations;

namespace PromptOps.Infrastructure.Evaluations;

/// <summary>
/// Default <see cref="IPendingDelegatedEvaluationStore"/> (ADR-0010/Phase 12) — an in-process,
/// TTL-evicted dictionary. Deliberately not backed by SQLite: a delegated evaluation is meant to
/// complete within one live conversation turn, so surviving a daemon restart mid-flight isn't a
/// requirement, and skipping a migration/table for something this short-lived keeps the change
/// small. Takes a <see cref="TimeProvider"/> (not a bespoke clock abstraction) so expiry is
/// testable without a real 10-minute wait.
/// </summary>
public sealed class InMemoryPendingDelegatedEvaluationStore(TimeProvider? timeProvider = null)
    : IPendingDelegatedEvaluationStore
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly ConcurrentDictionary<Guid, PendingDelegatedEvaluation> _pending = new();

    public Guid Create(Guid executionId, string prompt)
    {
        var correlationId = Guid.NewGuid();
        _pending[correlationId] = new PendingDelegatedEvaluation(executionId, prompt, Attempt: 1, ExpiresAt());
        return correlationId;
    }

    public bool TryGet(Guid correlationId, out PendingDelegatedEvaluation? pending)
    {
        if (_pending.TryGetValue(correlationId, out var found) && found.ExpiresAtUtc > _timeProvider.GetUtcNow())
        {
            pending = found;
            return true;
        }

        _pending.TryRemove(correlationId, out _);
        pending = null;
        return false;
    }

    public void Update(Guid correlationId, string prompt, int attempt)
    {
        _pending.AddOrUpdate(
            correlationId,
            addValueFactory: _ => throw new PendingEvaluationNotFoundException(correlationId),
            updateValueFactory: (_, existing) => existing with { Prompt = prompt, Attempt = attempt, ExpiresAtUtc = ExpiresAt() });
    }

    public void Remove(Guid correlationId) => _pending.TryRemove(correlationId, out _);

    private DateTimeOffset ExpiresAt() => _timeProvider.GetUtcNow().Add(Ttl);
}
