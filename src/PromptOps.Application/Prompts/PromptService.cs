using PromptOps.Domain.Prompts;

namespace PromptOps.Application.Prompts;

/// <summary>Application-layer use cases for the Prompt Repository (Phase 2).</summary>
public sealed class PromptService(IPromptRepository repository)
{
    public async Task<Prompt> CreatePromptAsync(
        string name,
        PromptMetadata? metadata = null,
        CancellationToken cancellationToken = default)
    {
        var prompt = Prompt.Create(name, metadata);
        await repository.AddAsync(prompt, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        return prompt;
    }

    public async Task<PromptVersion> CreateVersionAsync(
        Guid promptId,
        string content,
        string createdBy,
        string? changelogEntry = null,
        IReadOnlyList<string>? templateVariables = null,
        CancellationToken cancellationToken = default)
    {
        var prompt = await GetOrThrowAsync(promptId, cancellationToken);

        var version = prompt.CreateVersion(content, createdBy, changelogEntry, templateVariables);

        await repository.UpdateAsync(prompt, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        return version;
    }

    public async Task TagPromptAsync(
        Guid promptId,
        IReadOnlyList<string> tags,
        CancellationToken cancellationToken = default)
    {
        var prompt = await GetOrThrowAsync(promptId, cancellationToken);

        prompt.AddTags(tags);

        await repository.UpdateAsync(prompt, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
    }

    public async Task DeprecatePromptVersionAsync(
        Guid promptId,
        Guid versionId,
        CancellationToken cancellationToken = default)
    {
        var prompt = await GetOrThrowAsync(promptId, cancellationToken);
        var version = FindVersionOrThrow(prompt, versionId);

        version.Deprecate();

        await repository.UpdateAsync(prompt, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
    }

    public async Task AddPromptDependencyAsync(
        Guid promptId,
        Guid versionId,
        Guid targetPromptVersionId,
        PromptDependencyRelationship relationship,
        CancellationToken cancellationToken = default)
    {
        var prompt = await GetOrThrowAsync(promptId, cancellationToken);
        var version = FindVersionOrThrow(prompt, versionId);

        version.AddDependency(targetPromptVersionId, relationship);

        await repository.UpdateAsync(prompt, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
    }

    private async Task<Prompt> GetOrThrowAsync(Guid promptId, CancellationToken cancellationToken)
        => await repository.GetByIdAsync(promptId, cancellationToken)
           ?? throw new PromptNotFoundException(promptId);

    private static PromptVersion FindVersionOrThrow(Prompt prompt, Guid versionId)
        => prompt.Versions.FirstOrDefault(v => v.Id == versionId)
           ?? throw new PromptVersionNotFoundException(prompt.Id, versionId);
}
