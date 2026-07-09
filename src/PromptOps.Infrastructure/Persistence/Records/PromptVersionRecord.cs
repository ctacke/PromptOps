namespace PromptOps.Infrastructure.Persistence.Records;

public sealed class PromptVersionRecord
{
    public Guid Id { get; set; }
    public Guid PromptId { get; set; }
    public int VersionNumber { get; set; }
    public string Content { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
    public Guid? ParentVersionId { get; set; }
    public string? ChangelogEntry { get; set; }
    public List<string> TemplateVariables { get; set; } = [];
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public List<PromptDependencyRecord> Dependencies { get; set; } = [];
}
