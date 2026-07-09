using PromptOps.Application.Events;
using PromptOps.Application.Executions;
using PromptOps.Domain.Evaluations;

namespace PromptOps.Application.Evaluations;

/// <summary>Application-layer use cases for Human Evaluation (Phase 6) — what <c>/promptops rate</c> calls.</summary>
public sealed class HumanEvaluationService(
    IHumanEvaluationRepository repository,
    IExecutionRepository executionRepository,
    IDomainEventPublisher eventPublisher)
{
    public async Task<HumanEvaluation> SubmitAsync(
        Guid executionId,
        string evaluatorId,
        int correctness,
        int helpfulness,
        int architecture,
        int readability,
        int completeness,
        bool hallucinations,
        int confidence,
        int overallSatisfaction,
        string? notes = null,
        CancellationToken cancellationToken = default)
    {
        if (await executionRepository.GetByIdAsync(executionId, cancellationToken) is null)
            throw new ExecutionNotFoundException(executionId);

        var evaluation = HumanEvaluation.Submit(
            executionId, evaluatorId, correctness, helpfulness, architecture, readability,
            completeness, hallucinations, confidence, overallSatisfaction, notes);

        await repository.AddAsync(evaluation, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);

        foreach (var domainEvent in evaluation.DomainEvents.ToList())
        {
            await eventPublisher.PublishAsync(domainEvent, cancellationToken);
        }
        evaluation.ClearDomainEvents();

        return evaluation;
    }

    public Task<IReadOnlyList<HumanEvaluation>> GetByExecutionIdAsync(Guid executionId, CancellationToken cancellationToken = default)
        => repository.GetByExecutionIdAsync(executionId, cancellationToken);
}
