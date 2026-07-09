namespace PromptOps.Application.Prompts;

public sealed class PromptNotFoundException(Guid promptId)
    : Exception($"Prompt '{promptId}' was not found.")
{
    public Guid PromptId { get; } = promptId;
}

public sealed class PromptVersionNotFoundException(Guid promptId, Guid versionId)
    : Exception($"Prompt '{promptId}' has no version '{versionId}'.")
{
    public Guid PromptId { get; } = promptId;
    public Guid VersionId { get; } = versionId;
}
