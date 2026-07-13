using Microsoft.EntityFrameworkCore;
using PromptOps.Application.Refinement;
using PromptOps.Domain.Refinement;
using PromptOps.Infrastructure.Persistence.Mapping;

namespace PromptOps.Infrastructure.Persistence;

/// <summary>Persistence for <see cref="RefinementCandidate"/> (Phase 16b) — a normal multi-row table, unlike the singleton policy tables.</summary>
public sealed class RefinementCandidateRepository(PromptOpsDbContext db) : IRefinementCandidateRepository
{
    public async Task AddAsync(RefinementCandidate candidate, CancellationToken cancellationToken = default)
    {
        await db.RefinementCandidates.AddAsync(RefinementCandidateMapper.ToNewEntity(candidate), cancellationToken);
    }

    public async Task<RefinementCandidate?> GetByDraftVersionIdAsync(Guid draftVersionId, CancellationToken cancellationToken = default)
    {
        var entity = await db.RefinementCandidates.AsNoTracking()
            .FirstOrDefaultAsync(c => c.DraftVersionId == draftVersionId, cancellationToken);
        return entity is null ? null : RefinementCandidateMapper.ToDomain(entity);
    }

    public async Task<IReadOnlyList<RefinementCandidate>> GetAbEligibleByActiveVersionIdAsync(Guid activeVersionId, CancellationToken cancellationToken = default)
    {
        var status = RefinementCandidateStatus.AbEligible.ToString();
        var entities = await db.RefinementCandidates.AsNoTracking()
            .Where(c => c.ActiveVersionId == activeVersionId && c.Status == status)
            .ToListAsync(cancellationToken);
        return entities.Select(RefinementCandidateMapper.ToDomain).ToList();
    }

    public async Task UpdateAsync(RefinementCandidate candidate, CancellationToken cancellationToken = default)
    {
        var entity = await db.RefinementCandidates.FirstOrDefaultAsync(c => c.Id == candidate.Id, cancellationToken)
            ?? throw new InvalidOperationException($"RefinementCandidate '{candidate.Id}' must be added via {nameof(AddAsync)} before it can be updated.");

        RefinementCandidateMapper.ApplyChanges(entity, candidate);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => db.SaveChangesAsync(cancellationToken);
}
