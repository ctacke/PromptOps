using PromptOps.Domain.Promotion;

namespace PromptOps.Application.Promotion;

/// <summary>Persistence port for the single global <see cref="PromotionPolicy"/> singleton (Phase 11).</summary>
public interface IPromotionPolicyRepository
{
    /// <summary>The one policy row, if it's ever been created. <c>null</c> on a fresh daemon — <see cref="PromotionPolicyService"/> lazy-bootstraps a default the same way <c>ScoringService</c> does for <c>ScoringConfig</c>.</summary>
    Task<PromotionPolicy?> GetAsync(CancellationToken cancellationToken = default);

    Task AddAsync(PromotionPolicy policy, CancellationToken cancellationToken = default);

    Task UpdateAsync(PromotionPolicy policy, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
