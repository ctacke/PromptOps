namespace PromptOps.Infrastructure.Persistence.Records;

/// <summary>
/// EF Core persistence shape for <see cref="PromptOps.Domain.Prompts.Prompt"/>. Deliberately a
/// plain, mutable class distinct from the domain aggregate — the ORM's shape requirements never
/// leak into <c>Domain</c> (ADR-0002); <see cref="Mapping.PromptMapper"/> converts between the two.
/// </summary>
public sealed class PromptRecord
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public PromptMetadataRecord Metadata { get; set; } = new();
    public List<PromptVersionRecord> Versions { get; set; } = [];
}
