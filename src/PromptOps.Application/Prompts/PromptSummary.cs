namespace PromptOps.Application.Prompts;

/// <summary>Identity-only read — every prompt's id and name, nothing else. Used by <c>/promptops init</c> to check "does a prompt with this name already exist" before seeding, without loading metadata or version content.</summary>
public sealed record PromptSummary(Guid Id, string Name);
