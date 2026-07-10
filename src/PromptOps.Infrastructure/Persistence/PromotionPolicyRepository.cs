using Microsoft.EntityFrameworkCore;
using PromptOps.Application.Promotion;
using PromptOps.Domain.Promotion;
using PromptOps.Infrastructure.Persistence.Mapping;

namespace PromptOps.Infrastructure.Persistence;

/// <summary>A true singleton table — at most one row ever exists, so unlike <see cref="PromptRepository"/> there's no per-id identity map to maintain, just "the" row.</summary>
public sealed class PromotionPolicyRepository(PromptOpsDbContext db) : IPromotionPolicyRepository
{
    public async Task<PromotionPolicy?> GetAsync(CancellationToken cancellationToken = default)
    {
        var entity = await db.PromotionPolicies.FirstOrDefaultAsync(cancellationToken);
        return entity is null ? null : PromotionPolicyMapper.ToDomain(entity);
    }

    public async Task AddAsync(PromotionPolicy policy, CancellationToken cancellationToken = default)
    {
        await db.PromotionPolicies.AddAsync(PromotionPolicyMapper.ToNewEntity(policy), cancellationToken);
    }

    public async Task UpdateAsync(PromotionPolicy policy, CancellationToken cancellationToken = default)
    {
        var entity = await db.PromotionPolicies.FirstOrDefaultAsync(p => p.Id == policy.Id, cancellationToken)
            ?? throw new InvalidOperationException($"PromotionPolicy '{policy.Id}' must be added via {nameof(AddAsync)} before it can be updated.");

        PromotionPolicyMapper.ApplyChanges(entity, policy);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => db.SaveChangesAsync(cancellationToken);
}
