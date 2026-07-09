namespace PromptOps.Infrastructure.Persistence.Records;

/// <summary>
/// EF Core persistence shape for <see cref="PromptOps.Domain.Executions.ExecutionRecord"/>.
/// <see cref="PromptVersionId"/> is deliberately not a foreign key — see
/// <see cref="Configurations.ExecutionRecordEntityConfiguration"/>.
/// </summary>
public sealed class ExecutionRecordEntity
{
    public Guid Id { get; set; }
    public Guid PromptVersionId { get; set; }
    public string DeveloperId { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }

    public string Repository { get; set; } = string.Empty;
    public string? Branch { get; set; }
    public string? Commit { get; set; }
    public string? TaskId { get; set; }
    public List<string> ReferencedDocuments { get; set; } = [];
    public List<string> ReferencedADRs { get; set; } = [];
    public List<string> AcceptanceCriteria { get; set; } = [];
    public List<string> Languages { get; set; } = [];

    public Dictionary<string, string> Inputs { get; set; } = [];

    public string Status { get; set; } = string.Empty;
    public string? Output { get; set; }
    public long? ExecutionTimeMs { get; set; }
    public string? AiProviderId { get; set; }
    public string? Model { get; set; }
    public string? ModelParameters { get; set; }
    public List<string> FilesChanged { get; set; } = [];
    public int LinesAdded { get; set; }
    public int LinesDeleted { get; set; }

    public List<ToolUsageEntity> ToolUsage { get; set; } = [];
}
