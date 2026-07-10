using PromptOps.Application.Evaluations;
using PromptOps.Domain.Evaluations;

namespace PromptOps.Host.Endpoints;

/// <summary>Manages the single global <see cref="AIEvaluationPolicy"/> singleton — the on/off switch <c>AutoAIEvaluationTrigger</c> reads.</summary>
public static class AIEvaluationPolicyEndpoints
{
    public static void MapAIEvaluationPolicyEndpoints(this WebApplication app)
    {
        app.MapGet("/ai-evaluation-policy", async (AIEvaluationPolicyService service, CancellationToken cancellationToken) =>
        {
            var policy = await service.GetOrCreateDefaultAsync(cancellationToken);
            return Results.Ok(AIEvaluationPolicyResponse.From(policy));
        });

        app.MapPut("/ai-evaluation-policy", async (UpdateAIEvaluationPolicyRequest request, AIEvaluationPolicyService service, CancellationToken cancellationToken) =>
        {
            var mechanism = request.Mechanism is null
                ? AutoEvaluationMechanism.Daemon
                : Enum.Parse<AutoEvaluationMechanism>(request.Mechanism, ignoreCase: true);

            var policy = await service.UpdateAsync(request.AutoEvaluateOnFinish, mechanism, cancellationToken);
            return Results.Ok(AIEvaluationPolicyResponse.From(policy));
        });
    }
}

/// <summary><c>Mechanism</c> is optional and defaults to <see cref="AutoEvaluationMechanism.Daemon"/> when omitted (Phase 13) — every existing caller that only ever sent <c>autoEvaluateOnFinish</c> keeps behaving exactly as it does today.</summary>
internal sealed record UpdateAIEvaluationPolicyRequest(bool AutoEvaluateOnFinish, string? Mechanism = null);

internal sealed record AIEvaluationPolicyResponse(Guid Id, bool AutoEvaluateOnFinish, string Mechanism, DateTimeOffset UpdatedAt)
{
    public static AIEvaluationPolicyResponse From(AIEvaluationPolicy policy) => new(policy.Id, policy.AutoEvaluateOnFinish, policy.Mechanism.ToString(), policy.UpdatedAt);
}
