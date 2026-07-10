using PromptOps.Application.Embeddings;
using PromptOps.Application.Providers;
using PromptOps.Domain.Prompts;

namespace PromptOps.Application.Prompts;

/// <summary>
/// Application-layer use cases for the Prompt Repository (Phase 2). Also indexes an embedding
/// per <see cref="PromptVersion"/> (Phase 10) whenever its content or its prompt's tags change —
/// <see cref="SemanticRecommendationProvider"/> in <c>PromptOps.Infrastructure</c> is what
/// searches this index later; this is just where it gets kept up to date.
/// </summary>
public sealed class PromptService(IPromptRepository repository, IEmbeddingProvider embeddingProvider, IEmbeddingStore embeddingStore)
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
        await IndexEmbeddingAsync(prompt, version, cancellationToken);
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

        // Tags are part of the embedded text (see BuildEmbeddingText), so a re-tag makes the
        // latest version's stored embedding stale — refresh it. Versions before the latest one
        // keep whatever embedding they already had; only the latest is realistically ever
        // recommended (see PromptRecommendationCandidate's "best version" selection).
        if (prompt.LatestVersion is { } latestVersion)
            await IndexEmbeddingAsync(prompt, latestVersion, cancellationToken);
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

    /// <summary>
    /// Promotes a version to Active — the manual counterpart to the automatic, policy-driven
    /// promotion <c>AutoPromotionTrigger</c> performs in <c>PromptOps.Infrastructure</c> (Phase 11).
    /// Both paths funnel through the same <see cref="Prompt.ActivateVersion"/> domain method, so
    /// "exactly one Active version" is enforced identically either way.
    /// </summary>
    public async Task ActivateVersionAsync(
        Guid promptId,
        Guid versionId,
        CancellationToken cancellationToken = default)
    {
        var prompt = await GetOrThrowAsync(promptId, cancellationToken);
        FindVersionOrThrow(prompt, versionId);

        prompt.ActivateVersion(versionId);

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

    private async Task IndexEmbeddingAsync(Prompt prompt, PromptVersion version, CancellationToken cancellationToken)
    {
        var text = BuildEmbeddingText(prompt, version);
        var embedding = await embeddingProvider.EmbedAsync(text, cancellationToken);
        await embeddingStore.StoreAsync(version.Id, EmbeddingSubjectTypes.PromptVersion, embedding, cancellationToken);
    }

    /// <summary>What gets embedded for semantic search: everything that describes what the prompt version is *for*, not just its raw content — name and tags carry intent that the content text alone might not.</summary>
    private static string BuildEmbeddingText(Prompt prompt, PromptVersion version) => string.Join(
        " ",
        new[] { prompt.Name, prompt.Metadata.Description, string.Join(" ", prompt.Metadata.Tags), version.Content }
            .Where(part => !string.IsNullOrWhiteSpace(part)));

    public Task<PromptMetadataView?> GetMetadataAsync(Guid promptId, CancellationToken cancellationToken = default)
        => repository.GetMetadataAsync(promptId, cancellationToken);

    public Task<IReadOnlyList<PromptSummary>> ListAsync(CancellationToken cancellationToken = default)
        => repository.GetAllNamesAsync(cancellationToken);

    private async Task<Prompt> GetOrThrowAsync(Guid promptId, CancellationToken cancellationToken)
        => await repository.GetByIdAsync(promptId, cancellationToken)
           ?? throw new PromptNotFoundException(promptId);

    private static PromptVersion FindVersionOrThrow(Prompt prompt, Guid versionId)
        => prompt.Versions.FirstOrDefault(v => v.Id == versionId)
           ?? throw new PromptVersionNotFoundException(prompt.Id, versionId);
}
