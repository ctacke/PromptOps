using Microsoft.EntityFrameworkCore;
using PromptOps.Application.Executions;
using PromptOps.Domain.Executions;
using PromptOps.Infrastructure.Persistence.Mapping;
using PromptOps.Infrastructure.Persistence.Records;

namespace PromptOps.Infrastructure.Persistence;

/// <summary>Identity-mapped for the lifetime of one unit of work — see PromptRepository for why (ADR-0005 / docs/prompt-repository.md).</summary>
public sealed class ExecutionRepository(PromptOpsDbContext db) : IExecutionRepository
{
    private readonly Dictionary<Guid, ExecutionRecordEntity> _tracked = [];

    public async Task AddAsync(ExecutionRecord execution, CancellationToken cancellationToken = default)
    {
        var entity = ExecutionMapper.ToNewEntity(execution);
        await db.Executions.AddAsync(entity, cancellationToken);
        _tracked[execution.Id] = entity;
    }

    public async Task<ExecutionRecord?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!_tracked.TryGetValue(id, out var entity))
        {
            entity = await LoadFullEntityAsync(id, cancellationToken);
            if (entity is null)
                return null;

            _tracked[id] = entity;
        }

        return ExecutionMapper.ToDomain(entity);
    }

    public async Task<IReadOnlyList<ExecutionRecord>> GetByPromptVersionIdAsync(Guid promptVersionId, CancellationToken cancellationToken = default)
    {
        // SQLite can't ORDER BY DateTimeOffset in SQL (see LoadFullEntityAsync) — sort client-side after load.
        var entities = await db.Executions
            .Include(e => e.ToolUsage)
            .Where(e => e.PromptVersionId == promptVersionId)
            .ToListAsync(cancellationToken);

        foreach (var entity in entities)
        {
            entity.ToolUsage.Sort((a, b) => a.RecordedAt.CompareTo(b.RecordedAt));
            _tracked[entity.Id] = entity;
        }

        return entities.OrderBy(e => e.Timestamp).Select(ExecutionMapper.ToDomain).ToList();
    }

    public Task UpdateAsync(ExecutionRecord execution, CancellationToken cancellationToken = default)
    {
        if (!_tracked.TryGetValue(execution.Id, out var entity))
            throw new InvalidOperationException(
                $"Execution '{execution.Id}' must be loaded via {nameof(GetByIdAsync)} (in this same unit of work) before it can be updated.");

        ExecutionMapper.ApplyChanges(entity, execution);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => db.SaveChangesAsync(cancellationToken);

    private async Task<ExecutionRecordEntity?> LoadFullEntityAsync(Guid id, CancellationToken cancellationToken)
    {
        // SQLite can't ORDER BY DateTimeOffset in SQL, so ToolUsage is sorted client-side after
        // load (ExecutionMapper.ApplyChanges relies on a stable order across loads — see its docs).
        var entity = await db.Executions
            .Include(e => e.ToolUsage)
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

        entity?.ToolUsage.Sort((a, b) => a.RecordedAt.CompareTo(b.RecordedAt));
        return entity;
    }
}
