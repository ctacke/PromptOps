using System.Diagnostics;
using PromptOps.Application.Events;
using PromptOps.Application.Providers;
using PromptOps.Domain.Executions;

namespace PromptOps.Application.Executions;

/// <summary>Application-layer use cases for Execution Tracking (Phase 3).</summary>
public sealed class ExecutionService(
    IExecutionRepository repository,
    IDomainEventPublisher eventPublisher,
    IAIExecutionProvider aiExecutionProvider)
{
    /// <summary>Opens an execution. This is what a Claude Code hook's <c>SessionStart</c> calls, pushing context it alone can compute (ADR-0005 §9).</summary>
    public async Task<ExecutionRecord> StartExecutionAsync(
        Guid promptVersionId,
        string developerId,
        DevelopmentContext context,
        IReadOnlyDictionary<string, string>? inputs = null,
        CancellationToken cancellationToken = default)
    {
        var execution = ExecutionRecord.Start(promptVersionId, developerId, context, inputs);
        await repository.AddAsync(execution, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        return execution;
    }

    /// <summary>What a Claude Code hook's <c>PreToolUse</c>/<c>PostToolUse</c> calls to stream tool-usage stats.</summary>
    public async Task RecordToolUsageAsync(
        Guid executionId,
        string toolName,
        int count,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        var execution = await GetOrThrowAsync(executionId, cancellationToken);
        execution.RecordToolUsage(toolName, count, duration);
        await repository.UpdateAsync(execution, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
    }

    /// <summary>What a Claude Code hook's <c>Stop</c> calls, pushing the diff stats it alone can compute. Raises <see cref="ExecutionRecorded"/>.</summary>
    public async Task<ExecutionRecord> FinishExecutionAsync(
        Guid executionId,
        string? output,
        TimeSpan executionTime,
        string? aiProviderId,
        string? model,
        string? modelParameters,
        IReadOnlyList<string> filesChanged,
        int linesAdded,
        int linesDeleted,
        CancellationToken cancellationToken = default)
    {
        var execution = await GetOrThrowAsync(executionId, cancellationToken);
        execution.Finish(output, executionTime, aiProviderId, model, modelParameters, filesChanged, linesAdded, linesDeleted);

        await repository.UpdateAsync(execution, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        await PublishDomainEventsAsync(execution, cancellationToken);

        return execution;
    }

    /// <summary>
    /// Executes a prompt via <see cref="IAIExecutionProvider"/> and records the result in one call.
    /// Distinct from the push-based Start/RecordToolUsage/Finish path above: that path exists because
    /// the real Claude Code integration runs locally and reports back what already happened (ADR-0005
    /// §9); this path is for callers where the daemon itself drives execution (e.g. Phase 7's AI
    /// evaluation pipeline, built on the same <see cref="IAIExecutionProvider"/> abstraction).
    /// </summary>
    public async Task<ExecutionRecord> ExecuteAndRecordAsync(
        Guid promptVersionId,
        string developerId,
        DevelopmentContext context,
        string promptContent,
        IReadOnlyDictionary<string, string>? inputs = null,
        CancellationToken cancellationToken = default)
    {
        var execution = ExecutionRecord.Start(promptVersionId, developerId, context, inputs);
        await repository.AddAsync(execution, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);

        var stopwatch = Stopwatch.StartNew();
        var output = await aiExecutionProvider.ExecuteAsync(promptContent, inputs ?? new Dictionary<string, string>(), cancellationToken);
        stopwatch.Stop();

        execution.Finish(output, stopwatch.Elapsed, aiExecutionProvider.Name, model: null, modelParameters: null, filesChanged: [], linesAdded: 0, linesDeleted: 0);

        await repository.UpdateAsync(execution, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        await PublishDomainEventsAsync(execution, cancellationToken);

        return execution;
    }

    public Task<ExecutionRecord?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => repository.GetByIdAsync(id, cancellationToken);

    private async Task<ExecutionRecord> GetOrThrowAsync(Guid id, CancellationToken cancellationToken)
        => await repository.GetByIdAsync(id, cancellationToken) ?? throw new ExecutionNotFoundException(id);

    private async Task PublishDomainEventsAsync(ExecutionRecord execution, CancellationToken cancellationToken)
    {
        foreach (var domainEvent in execution.DomainEvents.ToList())
        {
            await eventPublisher.PublishAsync(domainEvent, cancellationToken);
        }

        execution.ClearDomainEvents();
    }
}
