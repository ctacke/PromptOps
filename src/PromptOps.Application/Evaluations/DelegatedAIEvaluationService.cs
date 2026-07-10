using PromptOps.Application.Events;
using PromptOps.Application.Executions;
using PromptOps.Domain.Evaluations;

namespace PromptOps.Application.Evaluations;

/// <summary>
/// Client-delegated AI evaluation (ADR-0010/Phase 12): a two-call replacement for the MCP
/// <c>sampling/createMessage</c> capability, which was deprecated (SEP-2577) before any mainstream
/// client — including Claude Code — implemented the client side of it. <see cref="PrepareAsync"/>
/// builds the judge prompt and hands it back to the calling MCP client to answer with its own
/// model/session; <see cref="SubmitAsync"/> parses that answer and either persists an
/// <see cref="AIEvaluation"/> or asks for a corrected retry — reusing <see cref="JudgePromptBuilder"/>/
/// <see cref="JudgeResponseParser"/>, the same building blocks the autonomous
/// <c>AIJudgeEvaluationProvider</c> path uses, so both flows judge identically.
/// </summary>
public sealed class DelegatedAIEvaluationService(
    IExecutionRepository executionRepository,
    IPendingDelegatedEvaluationStore pendingStore,
    IAIEvaluationRepository evaluationRepository,
    IDomainEventPublisher eventPublisher)
{
    private const string JudgeProviderId = "client-delegated";

    public async Task<DelegatedJudgePrompt> PrepareAsync(Guid executionId, CancellationToken cancellationToken = default)
    {
        var execution = await executionRepository.GetByIdAsync(executionId, cancellationToken)
            ?? throw new ExecutionNotFoundException(executionId);

        var prompt = JudgePromptBuilder.Build(execution);
        var correlationId = pendingStore.Create(executionId, prompt);

        return new DelegatedJudgePrompt(correlationId, prompt);
    }

    public async Task<JudgeSubmissionResult> SubmitAsync(
        Guid correlationId, string response, CancellationToken cancellationToken = default)
    {
        if (!pendingStore.TryGet(correlationId, out var pending) || pending is null)
        {
            throw new PendingEvaluationNotFoundException(correlationId);
        }

        if (JudgeResponseParser.TryParse(response, out var parsed, out var parseError))
        {
            var evaluation = AIEvaluation.Record(
                pending.ExecutionId,
                judgeProviderId: JudgeProviderId,
                judgeModel: null,
                satisfiesAcceptanceCriteria: parsed!.SatisfiesAcceptanceCriteria,
                adrViolations: parsed.AdrViolations ?? [],
                ignoredRequirements: parsed.IgnoredRequirements ?? [],
                unnecessaryComplexityNotes: parsed.UnnecessaryComplexityNotes,
                suggestedPromptImprovements: parsed.SuggestedPromptImprovements ?? [],
                rawResponse: response);

            await evaluationRepository.AddAsync(evaluation, cancellationToken);
            await evaluationRepository.SaveChangesAsync(cancellationToken);

            foreach (var domainEvent in evaluation.DomainEvents.ToList())
            {
                await eventPublisher.PublishAsync(domainEvent, cancellationToken);
            }
            evaluation.ClearDomainEvents();

            pendingStore.Remove(correlationId);
            return JudgeSubmissionResult.Recorded(evaluation);
        }

        if (pending.Attempt >= JudgePromptBuilder.MaxAttempts)
        {
            pendingStore.Remove(correlationId);
            throw new AIJudgeResponseInvalidException(pending.ExecutionId, JudgePromptBuilder.MaxAttempts, parseError);
        }

        var correction = JudgePromptBuilder.AppendCorrection(pending.Prompt, response, parseError);
        pendingStore.Update(correlationId, correction, pending.Attempt + 1);
        return JudgeSubmissionResult.RetryNeeded(correction);
    }
}
