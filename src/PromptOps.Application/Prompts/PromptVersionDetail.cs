using PromptOps.Domain.Prompts;

namespace PromptOps.Application.Prompts;

/// <summary>A single version's full content, plus enough of its owning prompt's identity/tags to be useful standalone — the read shape for "show me this version's actual text," unlike <see cref="PromptMetadataView"/> which deliberately never touches content.</summary>
public sealed record PromptVersionDetail(
    Guid PromptId,
    string PromptName,
    Guid VersionId,
    int VersionNumber,
    string Content,
    PromptVersionStatus Status,
    IReadOnlyList<string> Tags);
