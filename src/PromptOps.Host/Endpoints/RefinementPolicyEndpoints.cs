using PromptOps.Application.Refinement;
using PromptOps.Domain.Refinement;

namespace PromptOps.Host.Endpoints;

/// <summary>Manages the single global <see cref="RefinementPolicy"/> singleton (Phase 16) — the on/off switch <c>PromptRefinementTrigger</c> reads before drafting an improved version.</summary>
public static class RefinementPolicyEndpoints
{
    public static void MapRefinementPolicyEndpoints(this WebApplication app)
    {
        app.MapGet("/refinement-policy", async (RefinementPolicyService service, CancellationToken cancellationToken) =>
        {
            var policy = await service.GetOrCreateDefaultAsync(cancellationToken);
            return Results.Ok(RefinementPolicyResponse.From(policy));
        });

        app.MapPut("/refinement-policy", async (UpdateRefinementPolicyRequest request, RefinementPolicyService service, CancellationToken cancellationToken) =>
        {
            try
            {
                var policy = await service.UpdateAsync(
                    request.AutoRefinementEnabled, request.SyntheticSampleSize, request.MinQualityDelta, request.AbExplorationRate, cancellationToken);
                return Results.Ok(RefinementPolicyResponse.From(policy));
            }
            catch (ArgumentOutOfRangeException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
    }
}

internal sealed record UpdateRefinementPolicyRequest(bool AutoRefinementEnabled, int SyntheticSampleSize, double MinQualityDelta, double AbExplorationRate);

internal sealed record RefinementPolicyResponse(Guid Id, bool AutoRefinementEnabled, int SyntheticSampleSize, double MinQualityDelta, double AbExplorationRate, DateTimeOffset UpdatedAt)
{
    public static RefinementPolicyResponse From(RefinementPolicy policy) => new(
        policy.Id, policy.AutoRefinementEnabled, policy.SyntheticSampleSize, policy.MinQualityDelta, policy.AbExplorationRate, policy.UpdatedAt);
}
