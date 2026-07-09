using PromptOps.Domain.Metrics;

namespace PromptOps.Application.Metrics;

public interface IEngineeringMetricsRepository
{
    Task AddAsync(EngineeringMetrics metrics, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EngineeringMetrics>> GetByExecutionIdAsync(Guid executionId, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
