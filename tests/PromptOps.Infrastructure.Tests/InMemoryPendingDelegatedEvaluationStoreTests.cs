using PromptOps.Application.Evaluations;
using PromptOps.Infrastructure.Evaluations;

namespace PromptOps.Infrastructure.Tests;

public class InMemoryPendingDelegatedEvaluationStoreTests
{
    [Fact]
    public void Create_then_TryGet_returns_the_pending_prompt()
    {
        var store = new InMemoryPendingDelegatedEvaluationStore();
        var executionId = Guid.NewGuid();

        var correlationId = store.Create(executionId, "the prompt");

        Assert.True(store.TryGet(correlationId, out var pending));
        Assert.Equal(executionId, pending!.ExecutionId);
        Assert.Equal("the prompt", pending.Prompt);
        Assert.Equal(1, pending.Attempt);
    }

    [Fact]
    public void TryGet_returns_false_for_an_unknown_correlation_id()
    {
        var store = new InMemoryPendingDelegatedEvaluationStore();

        Assert.False(store.TryGet(Guid.NewGuid(), out var pending));
        Assert.Null(pending);
    }

    [Fact]
    public void TryGet_returns_false_and_evicts_an_expired_entry()
    {
        var time = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var store = new InMemoryPendingDelegatedEvaluationStore(time);
        var correlationId = store.Create(Guid.NewGuid(), "the prompt");

        time.Advance(TimeSpan.FromMinutes(11));

        Assert.False(store.TryGet(correlationId, out var pending));
        Assert.Null(pending);

        // Still gone even after "time travels back" — eviction on the first failed lookup is permanent.
        time.Advance(TimeSpan.FromMinutes(-11));
        Assert.False(store.TryGet(correlationId, out _));
    }

    [Fact]
    public void Update_replaces_the_prompt_and_attempt_and_refreshes_expiry()
    {
        var time = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var store = new InMemoryPendingDelegatedEvaluationStore(time);
        var executionId = Guid.NewGuid();
        var correlationId = store.Create(executionId, "original prompt");

        store.Update(correlationId, "corrected prompt", 2);

        Assert.True(store.TryGet(correlationId, out var pending));
        Assert.Equal("corrected prompt", pending!.Prompt);
        Assert.Equal(2, pending.Attempt);
        Assert.Equal(executionId, pending.ExecutionId);
    }

    [Fact]
    public void Update_throws_for_an_unknown_correlation_id()
    {
        var store = new InMemoryPendingDelegatedEvaluationStore();

        Assert.Throws<PendingEvaluationNotFoundException>(() => store.Update(Guid.NewGuid(), "prompt", 2));
    }

    [Fact]
    public void Remove_makes_a_subsequent_TryGet_return_false()
    {
        var store = new InMemoryPendingDelegatedEvaluationStore();
        var correlationId = store.Create(Guid.NewGuid(), "the prompt");

        store.Remove(correlationId);

        Assert.False(store.TryGet(correlationId, out _));
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public void Advance(TimeSpan by) => _now = _now.Add(by);

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
