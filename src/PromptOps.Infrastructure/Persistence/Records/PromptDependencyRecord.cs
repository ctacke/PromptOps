namespace PromptOps.Infrastructure.Persistence.Records;

/// <summary>
/// A dependency link from a version onto another version. <see cref="TargetPromptVersionId"/> is
/// intentionally not mapped as a foreign key/navigation — the target may belong to a different
/// <see cref="PromptRecord"/> aggregate entirely (cross-prompt references are expected).
/// </summary>
public sealed class PromptDependencyRecord
{
    public Guid Id { get; set; }
    public Guid PromptVersionId { get; set; }
    public Guid TargetPromptVersionId { get; set; }
    public string Relationship { get; set; } = string.Empty;
}
