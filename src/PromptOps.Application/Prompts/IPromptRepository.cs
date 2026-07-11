using PromptOps.Domain.Prompts;

namespace PromptOps.Application.Prompts;

/// <summary>Persistence port for the <see cref="Prompt"/> aggregate. See ADR-0005 (SQLite, one shared database).</summary>
public interface IPromptRepository
{
    Task AddAsync(Prompt prompt, CancellationToken cancellationToken = default);

    Task<Prompt?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Loads the full <see cref="Prompt"/> aggregate that owns the given version — used by the auto-promotion trigger, which only has a <c>PromptVersionId</c> to start from (Phase 11).</summary>
    Task<Prompt?> GetByVersionIdAsync(Guid versionId, CancellationToken cancellationToken = default);

    /// <summary>Stages changes made to a previously-loaded aggregate. Call <see cref="SaveChangesAsync"/> to commit.</summary>
    Task UpdateAsync(Prompt prompt, CancellationToken cancellationToken = default);

    /// <summary>Metadata-only read — must not load version content.</summary>
    Task<PromptMetadataView?> GetMetadataAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Every prompt in the shared database, projected for ranking (Phase 9's <c>IRecommendationProvider</c>) — never loads version content.</summary>
    Task<IReadOnlyList<PromptRecommendationCandidate>> GetRecommendationCandidatesAsync(CancellationToken cancellationToken = default);

    /// <summary>Every prompt's id and name only — used to check for existing names (e.g. <c>/promptops init</c>'s de-dup check) without loading metadata or version content.</summary>
    Task<IReadOnlyList<PromptSummary>> GetAllNamesAsync(CancellationToken cancellationToken = default);

    /// <summary>Aggregate prompt/version counts, computed in SQL — never loads content or full aggregates.</summary>
    Task<PromptStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
