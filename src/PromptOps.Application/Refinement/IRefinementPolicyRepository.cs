using PromptOps.Domain.Refinement;

namespace PromptOps.Application.Refinement;

/// <summary>Persistence port for the single global <see cref="RefinementPolicy"/> singleton (Phase 16).</summary>
public interface IRefinementPolicyRepository
{
    /// <summary>The one policy row, if it's ever been created. <c>null</c> on a fresh daemon — <see cref="RefinementPolicyService"/> lazy-bootstraps a default, same pattern as <c>PromotionPolicyService</c>.</summary>
    Task<RefinementPolicy?> GetAsync(CancellationToken cancellationToken = default);

    Task AddAsync(RefinementPolicy policy, CancellationToken cancellationToken = default);

    Task UpdateAsync(RefinementPolicy policy, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
