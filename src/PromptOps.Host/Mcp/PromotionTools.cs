using System.ComponentModel;
using ModelContextProtocol.Server;
using PromptOps.Application.Promotion;
using PromptOps.Application.Prompts;
using PromptOps.Domain.Promotion;

namespace PromptOps.Host.Mcp;

/// <summary>
/// The manual activation primitive plus promotion-policy management, over MCP (Phase 11) — same
/// "let the agent call the service directly, no curl" rationale as every other tool class here.
/// </summary>
[McpServerToolType]
public sealed class PromotionTools(PromptService promptService, PromotionPolicyService policyService)
{
    [McpServerTool(Name = "activate_prompt_version")]
    [Description("Manually promotes a PromptVersion to Active, deprecating whichever version was previously Active for that prompt. This is the human-driven counterpart to automatic score-based promotion.")]
    public async Task<object> ActivatePromptVersion(
        [Description("The prompt's id.")] Guid promptId,
        [Description("The version id to activate.")] Guid versionId,
        CancellationToken cancellationToken = default)
    {
        await promptService.ActivateVersionAsync(promptId, versionId, cancellationToken);
        return new { promptId, versionId, status = "Active" };
    }

    [McpServerTool(Name = "get_promotion_policy")]
    [Description("Gets the current promotion policy: whether human evaluation is required, whether auto-promotion is enabled, and its threshold/margin.")]
    public async Task<object> GetPromotionPolicy(CancellationToken cancellationToken = default)
    {
        var policy = await policyService.GetOrCreateDefaultAsync(cancellationToken);
        return ToResponse(policy);
    }

    [McpServerTool(Name = "update_promotion_policy")]
    [Description("Updates the promotion policy. Enabling auto-promotion requires requireHumanEvaluation=false and at least one of minimumScoreThreshold/minimumMarginOverActive.")]
    public async Task<object> UpdatePromotionPolicy(
        [Description("Whether a human must submit an evaluation before a version can be considered for promotion.")] bool requireHumanEvaluation,
        [Description("Whether the daemon should automatically activate a version once its score clears the threshold/margin below.")] bool autoPromotionEnabled,
        [Description("Optional: absolute overall-score bar (0-100) that alone is sufficient to auto-promote.")] double? minimumScoreThreshold = null,
        [Description("Optional: how much a new version's score must beat the currently active version's score by, alone sufficient to auto-promote.")] double? minimumMarginOverActive = null,
        CancellationToken cancellationToken = default)
    {
        var policy = await policyService.UpdateAsync(requireHumanEvaluation, autoPromotionEnabled, minimumScoreThreshold, minimumMarginOverActive, cancellationToken);
        return ToResponse(policy);
    }

    private static object ToResponse(PromotionPolicy policy) => new
    {
        policy.Id,
        policy.RequireHumanEvaluation,
        policy.AutoPromotionEnabled,
        policy.MinimumScoreThreshold,
        policy.MinimumMarginOverActive,
        policy.UpdatedAt
    };
}
