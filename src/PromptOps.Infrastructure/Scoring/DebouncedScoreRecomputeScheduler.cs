using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PromptOps.Application.Scoring;

namespace PromptOps.Infrastructure.Scoring;

/// <summary>
/// Default <see cref="IScoreRecomputeScheduler"/>: a singleton, in-process debouncer. Each
/// <see cref="RequestRecompute"/> call for a given prompt version cancels that version's pending
/// timer (if any) and starts a new one; only once <see cref="DebounceWindow"/> passes without a
/// further request does the recompute actually run, in a fresh DI scope (this is a singleton, so
/// it can't hold a scoped <c>ScoringService</c> directly — <see cref="IServiceScopeFactory"/>
/// creates one on demand when the timer fires).
///
/// Rapid-fire events (a burst of <c>PostToolUse</c>-driven tool-usage metrics, several
/// <c>EngineeringMetrics</c> rows landing within seconds of each other) collapse into a single
/// recompute instead of one per event — the entire point of debouncing this rather than
/// recomputing synchronously inside every domain event handler.
/// </summary>
public sealed class DebouncedScoreRecomputeScheduler(
    IServiceScopeFactory scopeFactory,
    ILogger<DebouncedScoreRecomputeScheduler> logger) : IScoreRecomputeScheduler, IDisposable
{
    private static readonly TimeSpan DebounceWindow = TimeSpan.FromSeconds(10);

    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _pending = new();

    public void RequestRecompute(Guid promptVersionId)
    {
        var cts = new CancellationTokenSource();
        // AddOrUpdate always stores (and returns) `cts` here — the update branch's only job is to
        // cancel whatever timer was previously registered before this one replaces it.
        _pending.AddOrUpdate(promptVersionId, cts, (_, old) =>
        {
            old.Cancel();
            old.Dispose();
            return cts;
        });

        _ = RunAfterDelayAsync(promptVersionId, cts.Token);
    }

    private async Task RunAfterDelayAsync(Guid promptVersionId, CancellationToken token)
    {
        try
        {
            await Task.Delay(DebounceWindow, token);
        }
        catch (TaskCanceledException)
        {
            return; // superseded by a newer request for the same prompt version
        }

        _pending.TryRemove(promptVersionId, out _);

        try
        {
            using var scope = scopeFactory.CreateScope();
            var scoringService = scope.ServiceProvider.GetRequiredService<ScoringService>();
            await scoringService.RecomputeAsync(promptVersionId, cancellationToken: CancellationToken.None);
        }
        catch (Exception ex)
        {
            // A failed debounced recompute must never take down whatever triggered it (a hook
            // request, a metrics collection call) — it already returned its own response.
            logger.LogError(ex, "Debounced score recompute failed for prompt version {PromptVersionId}.", promptVersionId);
        }
    }

    public void Dispose()
    {
        foreach (var cts in _pending.Values)
        {
            cts.Cancel();
            cts.Dispose();
        }
        _pending.Clear();
    }
}
