using PromptOps.Application.Events;
using PromptOps.Application.Providers;
using PromptOps.Domain.Evaluations;

namespace PromptOps.Application.Evaluations;

/// <summary>
/// Application-layer use case for the AI Evaluation Pipeline (Phase 7). Unlike
/// <see cref="PromptOps.Application.Metrics.MetricsCollectionService"/>/<see cref="HumanEvaluationService"/>,
/// this doesn't check the execution exists before calling the provider — <see cref="IAIEvaluationProvider"/>
/// always needs the execution's content to build its judge prompt, so it already does that check
/// itself (and throws <see cref="PromptOps.Application.Executions.ExecutionNotFoundException"/> the
/// same way). Checking twice here would just be a second identical round trip with no behavioral
/// difference.
/// </summary>
public sealed class AIEvaluationService(
    IAIEvaluationProvider evaluationProvider,
    IAIEvaluationRepository repository,
    IDomainEventPublisher eventPublisher)
{
    public async Task<AIEvaluation> EvaluateAsync(
        Guid executionId,
        IReadOnlyDictionary<string, string>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        var evaluation = await evaluationProvider.EvaluateAsync(executionId, parameters ?? new Dictionary<string, string>(), cancellationToken);

        await repository.AddAsync(evaluation, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);

        foreach (var domainEvent in evaluation.DomainEvents.ToList())
        {
            await eventPublisher.PublishAsync(domainEvent, cancellationToken);
        }
        evaluation.ClearDomainEvents();

        return evaluation;
    }

    public Task<IReadOnlyList<AIEvaluation>> GetByExecutionIdAsync(Guid executionId, CancellationToken cancellationToken = default)
        => repository.GetByExecutionIdAsync(executionId, cancellationToken);
}
