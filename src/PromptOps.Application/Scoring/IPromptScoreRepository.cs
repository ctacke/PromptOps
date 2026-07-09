using PromptOps.Domain.Scoring;

namespace PromptOps.Application.Scoring;

public interface IPromptScoreRepository
{
    Task AddAsync(PromptScore score, CancellationToken cancellationToken = default);

    /// <summary>Full score history for a prompt version, chronological.</summary>
    Task<IReadOnlyList<PromptScore>> GetByPromptVersionIdAsync(Guid promptVersionId, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
