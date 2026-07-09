using PromptOps.Application.Executions;
using PromptOps.Domain.Executions;

namespace PromptOps.Plugins.Tests.Fakes;

internal sealed class FakeExecutionRepository : IExecutionRepository
{
    private readonly Dictionary<Guid, ExecutionRecord> _executions = [];

    public void Seed(ExecutionRecord execution) => _executions[execution.Id] = execution;

    public Task AddAsync(ExecutionRecord execution, CancellationToken cancellationToken = default)
    {
        _executions[execution.Id] = execution;
        return Task.CompletedTask;
    }

    public Task<ExecutionRecord?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => Task.FromResult(_executions.GetValueOrDefault(id));

    public Task<IReadOnlyList<ExecutionRecord>> GetByPromptVersionIdAsync(Guid promptVersionId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ExecutionRecord>>(_executions.Values.Where(e => e.PromptVersionId == promptVersionId).ToList());

    public Task UpdateAsync(ExecutionRecord execution, CancellationToken cancellationToken = default)
    {
        _executions[execution.Id] = execution;
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
