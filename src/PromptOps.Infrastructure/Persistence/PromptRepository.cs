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

    public async Task<IReadOnlyList<PromptRecommendationCandidate>> GetRecommendationCandidatesAsync(CancellationToken cancellationToken = default)
    {
        // Deliberately projects only version identity (id/number/status), never Content — same
        // "must not load version content" discipline as GetMetadataAsync, just across every prompt
        // instead of one. In-memory scan over tags (a JSON column, not SQL-filterable — see
        // docs/recommendations.md) is fine at this project's stated single-machine scale.
        var prompts = await db.Prompts
            .AsNoTracking()
            .Select(p => new
            {
                p.Id,
                p.Name,
                Tags = p.Metadata.Tags,
                Versions = p.Versions.Select(v => new { v.Id, v.VersionNumber, v.Status }).ToList()
            })
            .ToListAsync(cancellationToken);

        var candidates = new List<PromptRecommendationCandidate>();
        foreach (var prompt in prompts)
        {
            if (prompt.Versions.Count == 0)
                continue;

            var best = prompt.Versions
                .Where(v => v.Status == nameof(PromptVersionStatus.Active))
                .OrderByDescending(v => v.VersionNumber)
                .FirstOrDefault()
                ?? prompt.Versions.OrderByDescending(v => v.VersionNumber).First();

            candidates.Add(new PromptRecommendationCandidate(prompt.Id, prompt.Name, prompt.Tags, best.Id, best.VersionNumber));
        }

        return candidates;
    }

    public async Task<IReadOnlyList<PromptSummary>> GetAllNamesAsync(CancellationToken cancellationToken = default)
    {
        // Deliberately no .Include(p => p.Versions) or .Metadata — same "must not load version
        // content" discipline as GetMetadataAsync/GetRecommendationCandidatesAsync, just narrower
        // still since this doesn't even need metadata, only identity.
        var summaries = await db.Prompts
            .AsNoTracking()
            .Select(p => new PromptSummary(p.Id, p.Name))
            .ToListAsync(cancellationToken);

        return summaries;
    }

    /// <summary>Loads the full <see cref="Prompt"/> aggregate that owns the given version — used by <c>AutoPromotionTrigger</c> (Phase 11), which only has a <c>PromptVersionId</c> to start from.</summary>
    public async Task<Prompt?> GetByVersionIdAsync(Guid versionId, CancellationToken cancellationToken = default)
    {
        var trackedMatch = _tracked.Values.FirstOrDefault(r => r.Versions.Any(v => v.Id == versionId));
        if (trackedMatch is not null)
            return PromptMapper.ToDomain(trackedMatch);

        var record = await FullPromptQuery().FirstOrDefaultAsync(p => p.Versions.Any(v => v.Id == versionId), cancellationToken);
        if (record is null)
            return null;

        _tracked[record.Id] = record;
        return PromptMapper.ToDomain(record);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => db.SaveChangesAsync(cancellationToken);

    private Task<PromptRecord?> LoadFullRecordAsync(Guid id, CancellationToken cancellationToken)
        => FullPromptQuery().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    private IQueryable<PromptRecord> FullPromptQuery()
        => db.Prompts
            .Include(p => p.Metadata)
            .Include(p => p.Versions)
            .ThenInclude(v => v.Dependencies);
}
