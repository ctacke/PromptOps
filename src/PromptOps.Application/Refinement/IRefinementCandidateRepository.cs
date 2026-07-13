using PromptOps.Domain.Refinement;

namespace PromptOps.Application.Refinement;

/// <summary>Persistence port for <see cref="RefinementCandidate"/> (Phase 16b) — one row per auto-refined Draft tracked through the benchmark gate.</summary>
public interface IRefinementCandidateRepository
{
    Task AddAsync(RefinementCandidate candidate, CancellationToken cancellationToken = default);

    /// <summary>The candidate for a given Draft version, if one was ever recorded.</summary>
    Task<RefinementCandidate?> GetByDraftVersionIdAsync(Guid draftVersionId, CancellationToken cancellationToken = default);

    /// <summary>Every A/B-eligible candidate whose baseline is the given active version — Phase 16c's query for which Drafts may receive shadow traffic.</summary>
    Task<IReadOnlyList<RefinementCandidate>> GetAbEligibleByActiveVersionIdAsync(Guid activeVersionId, CancellationToken cancellationToken = default);

    Task UpdateAsync(RefinementCandidate candidate, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
