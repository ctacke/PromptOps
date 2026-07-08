namespace PromptOps.Application.Providers;

/// <summary>
/// Collects one engineering metric source (Sonar, build/test results, review activity) keyed
/// to an execution. See ADR-0003. First real implementations arrive in Phase 5.
/// </summary>
public interface IMetricCollector
{
    string Name { get; }

    Task CollectAsync(Guid executionId, CancellationToken cancellationToken = default);
}
