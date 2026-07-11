namespace PromptOps.Application.Prompts;

/// <summary>Aggregate counts across every prompt/version in the shared database — never loads content, same "must not load version content casually" discipline as <see cref="PromptMetadataView"/>/<see cref="PromptRecommendationCandidate"/>.</summary>
public sealed record PromptStatistics(int PromptCount, int VersionCount, IReadOnlyDictionary<string, int> VersionCountByStatus);
