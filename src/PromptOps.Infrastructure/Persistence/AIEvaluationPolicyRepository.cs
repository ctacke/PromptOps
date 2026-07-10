using Microsoft.EntityFrameworkCore;
using PromptOps.Application.Evaluations;
using PromptOps.Domain.Evaluations;
using PromptOps.Infrastructure.Persistence.Mapping;

namespace PromptOps.Infrastructure.Persistence;

/// <summary>A true singleton table — at most one row ever exists, same pattern as <see cref="PromotionPolicyRepository"/>.</summary>
public sealed class AIEvaluationPolicyRepository(PromptOpsDbContext db) : IAIEvaluationPolicyRepository
{
    public async Task<AIEvaluationPolicy?> GetAsync(CancellationToken cancellationToken = default)
    {
        var entity = await db.AIEvaluationPolicies.FirstOrDefaultAsync(cancellationToken);
        return entity is null ? null : AIEvaluationPolicyMapper.ToDomain(entity);
    }

    public async Task AddAsync(AIEvaluationPolicy policy, CancellationToken cancellationToken = default)
    {
        await db.AIEvaluationPolicies.AddAsync(AIEvaluationPolicyMapper.ToNewEntity(policy), cancellationToken);
    }

    public async Task UpdateAsync(AIEvaluationPolicy policy, CancellationToken cancellationToken = default)
    {
        var entity = await db.AIEvaluationPolicies.FirstOrDefaultAsync(p => p.Id == policy.Id, cancellationToken)
            ?? throw new InvalidOperationException($"AIEvaluationPolicy '{policy.Id}' must be added via {nameof(AddAsync)} before it can be updated.");

        AIEvaluationPolicyMapper.ApplyChanges(entity, policy);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => db.SaveChangesAsync(cancellationToken);
}
