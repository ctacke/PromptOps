namespace PromptOps.Infrastructure.Persistence.Records;

public sealed class ToolUsageEntity
{
    public Guid Id { get; set; }
    public Guid ExecutionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Count { get; set; }
    public long DurationMs { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
}
