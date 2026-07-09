namespace PromptOps.Domain.Executions;

/// <summary>One recorded use of a tool during an execution. <see cref="RecordedAt"/> exists purely to give repeated recordings a stable order — it's not user-facing data.</summary>
public sealed record ToolUsage(string Name, int Count, TimeSpan Duration, DateTimeOffset RecordedAt);
