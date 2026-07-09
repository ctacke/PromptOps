using PromptOps.Domain.Prompts;

namespace PromptOps.Application.Prompts;

/// <summary>
/// A read projection of a prompt's identity + metadata only — no version content. Proves
/// metadata is queryable independently of content (Phase 2 acceptance criterion); the
/// repository implementation must not touch the version/content table to produce this.
/// </summary>
public sealed record PromptMetadataView(Guid PromptId, string Name, PromptMetadata Metadata);
