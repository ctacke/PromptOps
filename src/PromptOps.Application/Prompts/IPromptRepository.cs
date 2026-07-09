using PromptOps.Domain.Prompts;

namespace PromptOps.Application.Prompts;

/// <summary>Persistence port for the <see cref="Prompt"/> aggregate. See ADR-0005 (SQLite, one shared database).</summary>
public interface IPromptRepository
{
    Task AddAsync(Prompt prompt, CancellationToken cancellationToken = default);

    Task<Prompt?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Stages changes made to a previously-loaded aggregate. Call <see cref="SaveChangesAsync"/> to commit.</summary>
    Task UpdateAsync(Prompt prompt, CancellationToken cancellationToken = default);

    /// <summary>Metadata-only read — must not load version content.</summary>
    Task<PromptMetadataView?> GetMetadataAsync(Guid id, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
