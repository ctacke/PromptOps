using System.ComponentModel;
using ModelContextProtocol.Server;
using PromptOps.Application.Refinement;
using PromptOps.Domain.Refinement;

namespace PromptOps.Host.Mcp;

/// <summary>
/// Refinement-policy management over MCP (Phase 16) — same "let the agent call the service directly,
/// no curl" rationale as every other tool class here.
/// </summary>
[McpServerToolType]
public sealed class RefinementTools(RefinementPolicyService policyService)
{
    [McpServerTool(Name = "get_refinement_policy")]
    [Description("Gets the current refinement policy: whether the daemon automatically drafts an improved prompt version from AI-judge suggestions.")]
    public async Task<object> GetRefinementPolicy(CancellationToken cancellationToken = default)
    {
        var policy = await policyService.GetOrCreateDefaultAsync(cancellationToken);
        return ToResponse(policy);
    }

    [McpServerTool(Name = "update_refinement_policy")]
    [Description("Configures automatic prompt refinement. autoRefinementEnabled: when on, each recorded AI evaluation with suggestions drafts a new Draft version. syntheticSampleSize: how many generated scenarios to benchmark a draft against before it can receive real traffic (0 disables benchmarking, leaving drafts for manual review). minQualityDelta: how much a draft must beat the active version by on that benchmark to become A/B-eligible. abExplorationRate: fraction (0-1) of matching sessions routed to an A/B-eligible draft so it earns a real score (0 disables shadow traffic).")]
    public async Task<object> UpdateRefinementPolicy(
        [Description("Whether the daemon should automatically draft an improved version from AI-judge suggestions.")] bool autoRefinementEnabled,
        [Description("Number of synthetic scenarios to grade a draft against (0 disables the benchmark pre-screen).")] int syntheticSampleSize = 0,
        [Description("How much a draft must beat the active version's benchmark score by to become A/B-eligible.")] double minQualityDelta = 0,
        [Description("Fraction (0-1) of matching sessions routed to an A/B-eligible draft for live evaluation (0 disables shadow traffic).")] double abExplorationRate = 0,
        CancellationToken cancellationToken = default)
    {
        var policy = await policyService.UpdateAsync(autoRefinementEnabled, syntheticSampleSize, minQualityDelta, abExplorationRate, cancellationToken);
        return ToResponse(policy);
    }

    private static object ToResponse(RefinementPolicy policy) => new
    {
        policy.Id,
        policy.AutoRefinementEnabled,
        policy.SyntheticSampleSize,
        policy.MinQualityDelta,
        policy.AbExplorationRate,
        policy.UpdatedAt
    };
}
