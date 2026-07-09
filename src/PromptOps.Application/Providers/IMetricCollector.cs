using PromptOps.Domain.Metrics;

namespace PromptOps.Application.Providers;

/// <summary>
/// Collects one engineering metric source (Sonar, build/test results, review activity) keyed
/// to an execution. See ADR-0003.
///
/// <paramref name="parameters"/> carries whatever the collector needs beyond the execution id:
/// network-reaching collectors (e.g. Sonar) mostly ignore it and use their own configuration,
/// while collectors that depend on externally-pushed content the daemon has no filesystem access
/// to (e.g. trx/Cobertura XML — ADR-0005 §9) read it from here. A collector that doesn't
/// recognize/need what's in <paramref name="parameters"/> returns <c>null</c> — "nothing to
/// report" — rather than throwing, so <c>MetricsCollectionService</c> can safely fan the same
/// call out to every registered collector without each one needing to know about the others.
/// </summary>
public interface IMetricCollector
{
    string Name { get; }

    Task<EngineeringMetrics?> CollectAsync(
        Guid executionId,
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken cancellationToken = default);
}
