using Microsoft.EntityFrameworkCore;
using PromptOps.Application.Refinement;
using PromptOps.Domain.Refinement;
using PromptOps.Infrastructure.Persistence.Mapping;

namespace PromptOps.Infrastructure.Persistence;

/// <summary>A true singleton table — at most one row ever exists, same pattern as <see cref="PromotionPolicyRepository"/>.</summary>
public sealed class RefinementPolicyRepository(PromptOpsDbContext db) : IRefinementPolicyRepository
{
    public async Task<RefinementPolicy?> GetAsync(CancellationToken cancellationToken = default)
    {
        var entity = await db.RefinementPolicies.FirstOrDefaultAsync(cancellationToken);
        return entity is null ? null : RefinementPolicyMapper.ToDomain(entity);
    }

    public async Task AddAsync(RefinementPolicy policy, CancellationToken cancellationToken = default)
    {
        await db.RefinementPolicies.AddAsync(RefinementPolicyMapper.ToNewEntity(policy), cancellationToken);
    }

    public async Task UpdateAsync(RefinementPolicy policy, CancellationToken cancellationToken = default)
    {
        var entity = await db.RefinementPolicies.FirstOrDefaultAsync(p => p.Id == policy.Id, cancellationToken)
            ?? throw new InvalidOperationException($"RefinementPolicy '{policy.Id}' must be added via {nameof(AddAsync)} before it can be updated.");

        RefinementPolicyMapper.ApplyChanges(entity, policy);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => db.SaveChangesAsync(cancellationToken);
}
