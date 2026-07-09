using PromptOps.Application.Events;
using PromptOps.Application.Executions;
using PromptOps.Application.Providers;
using PromptOps.Domain.Metrics;

namespace PromptOps.Application.Metrics;

/// <summary>
/// Fans a single collection request out to every registered <see cref="IMetricCollector"/>
/// (resolved via DI as <c>IEnumerable&lt;IMetricCollector&gt;</c>) and persists whatever comes
/// back non-null. Which collectors run is entirely a function of what's registered in DI —
/// adding or removing a collector plugin never touches this class (Phase 5 acceptance criterion:
/// "a config change to the daemon, not a code change").
/// </summary>
public sealed class MetricsCollectionService(
    IEnumerable<IMetricCollector> collectors,
    IEngineeringMetricsRepository metricsRepository,
    IExecutionRepository executionRepository,
    IDomainEventPublisher eventPublisher)
{
    public async Task<IReadOnlyList<EngineeringMetrics>> CollectAsync(
        Guid executionId,
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken cancellationToken = default)
    {
        if (await executionRepository.GetByIdAsync(executionId, cancellationToken) is null)
            throw new ExecutionNotFoundException(executionId);

        var collected = new List<EngineeringMetrics>();
        foreach (var collector in collectors)
        {
            var metrics = await collector.CollectAsync(executionId, parameters, cancellationToken);
            if (metrics is null) continue;

            await metricsRepository.AddAsync(metrics, cancellationToken);
            collected.Add(metrics);
        }

        if (collected.Count == 0) return collected;

        await metricsRepository.SaveChangesAsync(cancellationToken);

        foreach (var metrics in collected)
        {
            foreach (var domainEvent in metrics.DomainEvents.ToList())
            {
                await eventPublisher.PublishAsync(domainEvent, cancellationToken);
            }
            metrics.ClearDomainEvents();
        }

        return collected;
    }

    public Task<IReadOnlyList<EngineeringMetrics>> GetByExecutionIdAsync(Guid executionId, CancellationToken cancellationToken = default)
        => metricsRepository.GetByExecutionIdAsync(executionId, cancellationToken);
}
