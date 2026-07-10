using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PromptOps.Application.Evaluations;
using PromptOps.Application.Events;
using PromptOps.Domain.Evaluations;
using PromptOps.Domain.Executions;

namespace PromptOps.Infrastructure.Evaluations;

/// <summary>
/// Reacts to <see cref="ExecutionRecorded"/> (fires exactly once, from <c>ExecutionRecord.Finish()</c>)
/// alongside <c>ScoreRecomputeTrigger</c> — <see cref="Application.Events.DomainEventPublisher"/> resolves
/// and awaits every registered handler for an event type in turn, so this one must return quickly
/// rather than block the <c>/executions/{id}/finish</c> response on a multi-attempt LLM judge call.
/// The actual evaluation runs in a detached background task with its own DI scope. Gated behind
/// <see cref="AIEvaluationPolicy"/>'s <c>AutoEvaluateOnFinish</c> — off by default, since the judge
/// is a real (eventually paid) LLM call, not something to fire unconditionally on every execution.
/// Also gated on <c>Mechanism == Daemon</c> (Phase 13): when the policy instead selects
/// <c>ClientHook</c>, the per-repo plugin's <c>SessionEnd</c> hook is entirely responsible for
/// automatic evaluation and this trigger must stay silent, or every execution would be judged twice.
/// </summary>
public sealed class AutoAIEvaluationTrigger(
    IServiceScopeFactory scopeFactory,
    IAIEvaluationPolicyRepository policyRepository,
    ILogger<AutoAIEvaluationTrigger> logger) : IDomainEventHandler<ExecutionRecorded>
{
    public async Task HandleAsync(ExecutionRecorded domainEvent, CancellationToken cancellationToken = default)
    {
        var policy = await policyRepository.GetAsync(cancellationToken);
        if (policy is not { AutoEvaluateOnFinish: true, Mechanism: AutoEvaluationMechanism.Daemon })
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<AIEvaluationService>();
                await service.EvaluateAsync(domainEvent.ExecutionId, cancellationToken: CancellationToken.None);
            }
            catch (Exception ex)
            {
                // Same discipline as DebouncedScoreRecomputeScheduler: a failed background
                // evaluation must never surface back to whatever finished the execution.
                logger.LogError(ex, "Automatic AI evaluation failed for execution {ExecutionId}.", domainEvent.ExecutionId);
            }
        });
    }
}
