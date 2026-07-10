namespace PromptOps.Application.Evaluations;

public sealed class PendingEvaluationNotFoundException(Guid correlationId)
    : Exception($"No pending delegated evaluation found for correlation id '{correlationId}' (it may have expired or already been submitted).")
{
    public Guid CorrelationId { get; } = correlationId;
}
