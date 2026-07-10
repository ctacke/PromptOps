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
            var policy = await service.UpdateAsync(request.AutoEvaluateOnFinish, cancellationToken);
            return Results.Ok(AIEvaluationPolicyResponse.From(policy));
        });
    }
}

internal sealed record UpdateAIEvaluationPolicyRequest(bool AutoEvaluateOnFinish);

internal sealed record AIEvaluationPolicyResponse(Guid Id, bool AutoEvaluateOnFinish, DateTimeOffset UpdatedAt)
{
    public static AIEvaluationPolicyResponse From(AIEvaluationPolicy policy) => new(policy.Id, policy.AutoEvaluateOnFinish, policy.UpdatedAt);
}
