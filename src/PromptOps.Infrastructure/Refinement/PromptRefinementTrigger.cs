using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PromptOps.Application.Events;
using PromptOps.Application.Refinement;
using PromptOps.Domain.Evaluations;

namespace PromptOps.Infrastructure.Refinement;

/// <summary>
/// Reacts to <see cref="AIEvaluationRecorded"/> (Phase 16a) — a second handler for that event
/// alongside <c>ScoreRecomputeTrigger</c>. When the policy's <c>AutoRefinementEnabled</c> is on, it
/// drafts an improved <c>PromptVersion</c> from the evaluation's <c>SuggestedPromptImprovements</c>
/// via <see cref="PromptRefinementService"/>, so the existing <c>AutoPromotionTrigger</c> finally
/// has a Draft candidate to consider.
///
/// Mirrors <c>AutoAIEvaluationTrigger</c>: a fast synchronous policy check (so it never blocks the
/// event-publish chain that fires from <c>AIEvaluationService</c>/<c>DelegatedAIEvaluationService</c>),
/// then the real work — which includes an LLM call to rewrite the prompt — in a detached background
/// task with its own DI scope and <see cref="CancellationToken.None"/>, wrapped in a
/// log-and-swallow catch so a refinement failure never surfaces to whatever recorded the evaluation.
/// </summary>
public sealed class PromptRefinementTrigger(
    IServiceScopeFactory scopeFactory,
    IRefinementPolicyRepository policyRepository,
    ILogger<PromptRefinementTrigger> logger) : IDomainEventHandler<AIEvaluationRecorded>
{
    public async Task HandleAsync(AIEvaluationRecorded domainEvent, CancellationToken cancellationToken = default)
    {
        var policy = await policyRepository.GetAsync(cancellationToken);
        if (policy is not { AutoRefinementEnabled: true })
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var refinement = scope.ServiceProvider.GetRequiredService<PromptRefinementService>();
                var result = await refinement.RefineFromEvaluationAsync(
                    domainEvent.EvaluationId, domainEvent.ExecutionId, CancellationToken.None);

                if (result.Outcome != RefinementOutcome.Drafted)
                {
                    logger.LogDebug(
                        "No refinement draft for AI evaluation {EvaluationId} (execution {ExecutionId}): {Outcome}.",
                        domainEvent.EvaluationId, domainEvent.ExecutionId, result.Outcome);
                    return;
                }

                logger.LogInformation(
                    "Auto-refined prompt {PromptId}: drafted new PromptVersion {DraftVersionId} from AI evaluation {EvaluationId}.",
                    result.PromptId, result.DraftVersionId, domainEvent.EvaluationId);

                // Phase 16b: run the synthetic-benchmark pre-screen on the fresh draft in the same
                // detached task — a draft that regresses is deprecated here and never reaches real work.
                var benchmark = scope.ServiceProvider.GetRequiredService<PromptBenchmarkService>();
                var gate = await benchmark.BenchmarkCandidateAsync(result.PromptId!.Value, result.DraftVersionId!.Value, CancellationToken.None);
                logger.LogInformation(
                    "Benchmark gate for PromptVersion {DraftVersionId}: {Outcome} (active {ActiveScore}, candidate {CandidateScore}).",
                    result.DraftVersionId, gate.Outcome, gate.Comparison?.ActiveScore, gate.Comparison?.CandidateScore);
            }
            catch (Exception ex)
            {
                // Same discipline as AutoAIEvaluationTrigger: a failed background refinement must
                // never surface back to whatever recorded the evaluation.
                logger.LogError(ex, "Automatic prompt refinement failed for AI evaluation {EvaluationId}.", domainEvent.EvaluationId);
            }
        }, CancellationToken.None);
    }
}
