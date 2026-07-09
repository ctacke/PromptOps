namespace PromptOps.Application.Prompts;

/// <summary>
/// A lightweight read projection for ranking (Phase 9) — identity, tags, and which specific
/// version would actually be recommended, without loading version content. The candidate version
/// is the highest-numbered <c>Active</c> version if one exists, otherwise the highest-numbered
/// version overall (recommending a <c>Draft</c>/<c>Deprecated</c> version only when nothing better
/// is available beats recommending nothing).
/// </summary>
public sealed record PromptRecommendationCandidate(
    Guid PromptId,
    string Name,
    IReadOnlyList<string> Tags,
    Guid PromptVersionId,
    int VersionNumber);
