using System.ComponentModel;
using ModelContextProtocol.Server;
using PromptOps.Application.Recommendations;

namespace PromptOps.Host.Mcp;

/// <summary>
/// Lets the agent get a prompt recommendation mid-session (ADR-0006: "get a recommendation" is
/// one of the canonical examples of why MCP tools exist alongside the ingestion API). The
/// developer supplies a task description, not tags — classification is internal
/// (<see cref="RecommendationService"/> does classify-then-recommend in one call), matching the
/// phase's explicit design goal that tag selection is never a separate user-facing step.
/// </summary>
[McpServerToolType]
public sealed class RecommendationTools(RecommendationService service)
{
    [McpServerTool(Name = "recommend_prompt")]
    [Description("Given a free-text task description, classifies it into activity tags and returns ranked PromptVersion recommendations with a stated rationale, drawing on history from every repo on this machine by default.")]
    public async Task<object> RecommendPrompt(
        [Description("Free-text description of the task at hand, e.g. 'fix a null reference exception in the login flow'.")] string taskDescription,
        [Description("Optional: restrict results to prompt versions with execution history in this specific repository. Omit to search across every repo on the machine (the default, and what makes recommendations useful in a brand-new repo).")] string? repository = null,
        [Description("Maximum number of recommendations to return.")] int limit = 5,
        CancellationToken cancellationToken = default)
    {
        var recommendations = await service.RecommendForTaskAsync(taskDescription, repository, limit, cancellationToken: cancellationToken);

        return recommendations.Select(r => new
        {
            r.Rank,
            r.RecommendedPromptVersionId,
            r.Rationale,
            r.SimilarityScore,
            r.QueryContext
        });
    }
}
