using PromptOps.Application.Events;
using PromptOps.Application.Executions;
using PromptOps.Application.Scoring;
using PromptOps.Domain.Evaluations;
using PromptOps.Domain.Executions;
using PromptOps.Domain.Metrics;

namespace PromptOps.Infrastructure.Scoring;

/// <summary>
/// Wires the "recompute-on-event" half of Phase 8's scoring engine: every domain event that
/// feeds a prompt version's score (an execution finishing, metrics/evaluations landing) requests
/// a debounced recompute for that prompt version. <see cref="ExecutionRecorded"/> already carries
/// its <c>PromptVersionId</c>; the other three only carry an <c>ExecutionId</c>, so this handler
/// looks the execution up to find which prompt version to recompute. Registered once per event
/// type it handles (see <c>ServiceCollectionExtensions</c>) — the same class, four registrations,
/// since <see cref="DomainEventPublisher"/> resolves handlers per closed <see cref="IDomainEventHandler{TEvent}"/>.
/// </summary>
public sealed class ScoreRecomputeTrigger(
    IScoreRecomputeScheduler scheduler,
    IExecutionRepository executionRepository) :
    IDomainEventHandler<ExecutionRecorded>,
    IDomainEventHandler<MetricsCollected>,
    IDomainEventHandler<HumanEvaluationSubmitted>,
    IDomainEventHandler<AIEvaluationRecorded>
{
    public Task HandleAsync(ExecutionRecorded domainEvent, CancellationToken cancellationToken = default)
    {
        scheduler.RequestRecompute(domainEvent.PromptVersionId);
        return Task.CompletedTask;
    }

    public Task HandleAsync(MetricsCollected domainEvent, CancellationToken cancellationToken = default)
        => RequestRecomputeForExecutionAsync(domainEvent.ExecutionId, cancellationToken);

    public Task HandleAsync(HumanEvaluationSubmitted domainEvent, CancellationToken cancellationToken = default)
        => RequestRecomputeForExecutionAsync(domainEvent.ExecutionId, cancellationToken);

    public Task HandleAsync(AIEvaluationRecorded domainEvent, CancellationToken cancellationToken = default)
        => RequestRecomputeForExecutionAsync(domainEvent.ExecutionId, cancellationToken);

    private async Task RequestRecomputeForExecutionAsync(Guid executionId, CancellationToken cancellationToken)
    {
        var execution = await executionRepository.GetByIdAsync(executionId, cancellationToken);
        if (execution is not null)
        {
            scheduler.RequestRecompute(execution.PromptVersionId);
        }
    }
}
