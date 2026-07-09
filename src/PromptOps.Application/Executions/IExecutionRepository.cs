using PromptOps.Domain.Executions;

namespace PromptOps.Application.Executions;

/// <summary>Persistence port for the <see cref="ExecutionRecord"/> aggregate. See ADR-0005 (SQLite, one shared database).</summary>
public interface IExecutionRepository
{
    Task AddAsync(ExecutionRecord execution, CancellationToken cancellationToken = default);

    Task<ExecutionRecord?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Stages changes made to a previously-loaded aggregate. Call <see cref="SaveChangesAsync"/> to commit.</summary>
    Task UpdateAsync(ExecutionRecord execution, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
