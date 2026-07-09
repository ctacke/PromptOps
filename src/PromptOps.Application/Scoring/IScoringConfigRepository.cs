using PromptOps.Domain.Scoring;

namespace PromptOps.Application.Scoring;

public interface IScoringConfigRepository
{
    Task AddAsync(ScoringConfig config, CancellationToken cancellationToken = default);

    Task<ScoringConfig?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>The highest-versioned config under <paramref name="name"/>, or null if none exists yet.</summary>
    Task<ScoringConfig?> GetLatestByNameAsync(string name, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ScoringConfig>> GetAllByNameAsync(string name, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
