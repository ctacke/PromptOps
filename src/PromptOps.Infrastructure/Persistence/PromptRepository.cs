using Microsoft.EntityFrameworkCore;
using PromptOps.Application.Prompts;
using PromptOps.Domain.Prompts;
using PromptOps.Infrastructure.Persistence.Mapping;
using PromptOps.Infrastructure.Persistence.Records;

namespace PromptOps.Infrastructure.Persistence;

/// <summary>
/// Scoped to one unit of work (one DI scope/request). Acts as an identity map for the lifetime of
/// that scope: once a <see cref="PromptRecord"/> is tracked (via <see cref="AddAsync"/> or
/// <see cref="GetByIdAsync"/>), later calls reuse that same tracked instance instead of re-querying
/// it. Re-querying an aggregate that's already tracked — even just to read it again — confuses EF
/// Core's change tracking for the separate-table owned <c>Metadata</c> entity and produces a bogus
/// <c>DbUpdateConcurrencyException</c> on the next save; reusing the tracked instance avoids that
/// entirely and is also just fewer round trips.
/// </summary>
public sealed class PromptRepository(PromptOpsDbContext db) : IPromptRepository
{
    private readonly Dictionary<Guid, PromptRecord> _tracked = [];

    public async Task AddAsync(Prompt prompt, CancellationToken cancellationToken = default)
    {
        var record = PromptMapper.ToNewRecord(prompt);
        await db.Prompts.AddAsync(record, cancellationToken);
        _tracked[prompt.Id] = record;
    }

    public async Task<Prompt?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!_tracked.TryGetValue(id, out var record))
        {
            record = await LoadFullRecordAsync(id, cancellationToken);
            if (record is null)
                return null;

            _tracked[id] = record;
        }

        return PromptMapper.ToDomain(record);
    }

    public Task UpdateAsync(Prompt prompt, CancellationToken cancellationToken = default)
    {
        if (!_tracked.TryGetValue(prompt.Id, out var record))
            throw new InvalidOperationException(
                $"Prompt '{prompt.Id}' must be loaded via {nameof(GetByIdAsync)} (in this same unit of work) before it can be updated.");

        PromptMapper.ApplyChanges(record, prompt);
        return Task.CompletedTask;
    }

    public async Task<PromptMetadataView?> GetMetadataAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // Deliberately no .Include(p => p.Versions) — this query must never touch version content.
        // AsNoTracking: a pure read projection; EF also refuses to track a projected owned entity
        // without its owner present in the same projection.
        var projection = await db.Prompts
            .AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => new { p.Id, p.Name, p.Metadata })
            .FirstOrDefaultAsync(cancellationToken);

        if (projection is null)
            return null;

        var metadata = new PromptMetadata
        {
            Description = projection.Metadata.Description,
            Tags = projection.Metadata.Tags,
            Categories = projection.Metadata.Categories,
            Owners = projection.Metadata.Owners,
            ExternalRefs = projection.Metadata.ExternalRefs
        };

        return new PromptMetadataView(projection.Id, projection.Name, metadata);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => db.SaveChangesAsync(cancellationToken);

    private Task<PromptRecord?> LoadFullRecordAsync(Guid id, CancellationToken cancellationToken)
        => db.Prompts
            .Include(p => p.Metadata)
            .Include(p => p.Versions)
            .ThenInclude(v => v.Dependencies)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
}
