namespace PromptOps.Domain.Prompts;

/// <summary>A link from a <see cref="PromptVersion"/> to another prompt version it depends on.</summary>
public sealed record PromptDependency(Guid TargetPromptVersionId, PromptDependencyRelationship Relationship);
