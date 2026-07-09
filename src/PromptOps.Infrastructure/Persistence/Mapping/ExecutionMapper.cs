using PromptOps.Domain.Executions;
using PromptOps.Infrastructure.Persistence.Records;

namespace PromptOps.Infrastructure.Persistence.Mapping;

/// <summary>
/// Converts between the <see cref="ExecutionRecord"/> aggregate and its EF Core persistence
/// records. <see cref="ApplyChanges"/> only ever appends new tool-usage rows — it relies on
/// <see cref="ExecutionRecord.ToolUsage"/> always being a superset (in the same order) of what's
/// already persisted, since tool usage is only ever appended, never edited or removed.
/// </summary>
internal static class ExecutionMapper
{
    public static ExecutionRecordEntity ToNewEntity(ExecutionRecord execution) => new()
    {
        Id = execution.Id,
        PromptVersionId = execution.PromptVersionId,
        DeveloperId = execution.DeveloperId,
        Timestamp = execution.Timestamp,
        Repository = execution.Context.Repository,
        Branch = execution.Context.Branch,
        Commit = execution.Context.Commit,
        TaskId = execution.Context.TaskId,
        ReferencedDocuments = execution.Context.ReferencedDocuments.ToList(),
        ReferencedADRs = execution.Context.ReferencedADRs.ToList(),
        AcceptanceCriteria = execution.Context.AcceptanceCriteria.ToList(),
        Languages = execution.Context.Languages.ToList(),
        Inputs = new Dictionary<string, string>(execution.Inputs),
        Status = execution.Status.ToString(),
        Output = execution.Output,
        ExecutionTimeMs = ToMilliseconds(execution.ExecutionTime),
        AiProviderId = execution.AiProviderId,
        Model = execution.Model,
        ModelParameters = execution.ModelParameters,
        FilesChanged = execution.FilesChanged.ToList(),
        LinesAdded = execution.LinesAdded,
        LinesDeleted = execution.LinesDeleted,
        ToolUsage = execution.ToolUsage.Select(u => ToNewToolUsageEntity(u, execution.Id)).ToList()
    };

    public static ExecutionRecord ToDomain(ExecutionRecordEntity entity)
    {
        var context = new DevelopmentContext
        {
            Repository = entity.Repository,
            Branch = entity.Branch,
            Commit = entity.Commit,
            TaskId = entity.TaskId,
            ReferencedDocuments = entity.ReferencedDocuments,
            ReferencedADRs = entity.ReferencedADRs,
            AcceptanceCriteria = entity.AcceptanceCriteria,
            Languages = entity.Languages
        };

        return ExecutionRecord.Rehydrate(
            entity.Id,
            entity.PromptVersionId,
            entity.DeveloperId,
            entity.Timestamp,
            context,
            entity.Inputs,
            Enum.Parse<ExecutionStatus>(entity.Status),
            entity.Output,
            entity.ExecutionTimeMs.HasValue ? TimeSpan.FromMilliseconds(entity.ExecutionTimeMs.Value) : null,
            entity.AiProviderId,
            entity.Model,
            entity.ModelParameters,
            entity.FilesChanged,
            entity.LinesAdded,
            entity.LinesDeleted,
            entity.ToolUsage.Select(t => new ToolUsage(t.Name, t.Count, TimeSpan.FromMilliseconds(t.DurationMs), t.RecordedAt)));
    }

    public static void ApplyChanges(ExecutionRecordEntity entity, ExecutionRecord execution)
    {
        entity.Status = execution.Status.ToString();
        entity.Output = execution.Output;
        entity.ExecutionTimeMs = ToMilliseconds(execution.ExecutionTime);
        entity.AiProviderId = execution.AiProviderId;
        entity.Model = execution.Model;
        entity.ModelParameters = execution.ModelParameters;
        entity.FilesChanged = execution.FilesChanged.ToList();
        entity.LinesAdded = execution.LinesAdded;
        entity.LinesDeleted = execution.LinesDeleted;

        foreach (var newToolUsage in execution.ToolUsage.Skip(entity.ToolUsage.Count))
        {
            entity.ToolUsage.Add(ToNewToolUsageEntity(newToolUsage, entity.Id));
        }
    }

    private static ToolUsageEntity ToNewToolUsageEntity(ToolUsage usage, Guid executionId) => new()
    {
        Id = Guid.NewGuid(),
        ExecutionId = executionId,
        Name = usage.Name,
        Count = usage.Count,
        DurationMs = (long)usage.Duration.TotalMilliseconds,
        RecordedAt = usage.RecordedAt
    };

    private static long? ToMilliseconds(TimeSpan? duration) => duration.HasValue ? (long)duration.Value.TotalMilliseconds : null;
}
