using PromptOps.Application.Promotion;
using PromptOps.Domain.Promotion;

namespace PromptOps.Host.Endpoints;

/// <summary>Manages the single global <see cref="PromotionPolicy"/> singleton (Phase 11) — the on/off switch and thresholds <c>AutoPromotionTrigger</c> reads.</summary>
public static class PromotionPolicyEndpoints
{
    public static void MapPromotionPolicyEndpoints(this WebApplication app)
    {
        app.MapGet("/promotion-policy", async (PromotionPolicyService service, CancellationToken cancellationToken) =>
        {
            var policy = await service.GetOrCreateDefaultAsync(cancellationToken);
            return Results.Ok(PromotionPolicyResponse.From(policy));
        });

        app.MapPut("/promotion-policy", async (UpdatePromotionPolicyRequest request, PromotionPolicyService service, CancellationToken cancellationToken) =>
        {
            try
            {
                var policy = await service.UpdateAsync(
                    request.RequireHumanEvaluation, request.AutoPromotionEnabled,
                    request.MinimumScoreThreshold, request.MinimumMarginOverActive, cancellationToken);
                return Results.Ok(PromotionPolicyResponse.From(policy));
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentOutOfRangeException)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
    }
}

internal sealed record UpdatePromotionPolicyRequest(
    bool RequireHumanEvaluation,
    bool AutoPromotionEnabled,
    double? MinimumScoreThreshold,
    double? MinimumMarginOverActive);

internal sealed record PromotionPolicyResponse(
    Guid Id,
    bool RequireHumanEvaluation,
    bool AutoPromotionEnabled,
    double? MinimumScoreThreshold,
    double? MinimumMarginOverActive,
    DateTimeOffset UpdatedAt)
{
    public static PromotionPolicyResponse From(PromotionPolicy policy) => new(
        policy.Id, policy.RequireHumanEvaluation, policy.AutoPromotionEnabled,
        policy.MinimumScoreThreshold, policy.MinimumMarginOverActive, policy.UpdatedAt);
}
