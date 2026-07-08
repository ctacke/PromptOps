namespace PromptOps.Domain.Prompts;

/// <summary>
/// Aggregate root for a prompt treated as a versioned engineering asset. Version content is
/// immutable once created; changes are expressed as new <see cref="PromptVersion"/>s linked
/// via lineage, not edits to existing ones.
/// </summary>
public sealed class Prompt
{
    private readonly List<PromptVersion> _versions = [];

    public Guid Id { get; }
    public string Name { get; private set; }
    public PromptMetadata Metadata { get; private set; }
    public DateTimeOffset CreatedAt { get; }
    public IReadOnlyList<PromptVersion> Versions => _versions.AsReadOnly();
    public PromptVersion? LatestVersion => _versions.Count == 0 ? null : _versions[^1];

    private Prompt(Guid id, string name, PromptMetadata metadata, DateTimeOffset createdAt)
    {
        Id = id;
        Name = name;
        Metadata = metadata;
        CreatedAt = createdAt;
    }

    public static Prompt Create(string name, PromptMetadata? metadata = null, DateTimeOffset? createdAt = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Prompt name is required.", nameof(name));

        return new Prompt(Guid.NewGuid(), name, metadata ?? PromptMetadata.Empty, createdAt ?? DateTimeOffset.UtcNow);
    }

    /// <summary>Reconstructs a prompt and its versions from persisted state (e.g. by a repository). Rejects corrupt data such as duplicate version numbers.</summary>
    public static Prompt Rehydrate(Guid id, string name, PromptMetadata metadata, DateTimeOffset createdAt, IEnumerable<PromptVersion> versions)
    {
        var prompt = new Prompt(id, name, metadata, createdAt);

        var seenVersionNumbers = new HashSet<int>();
        foreach (var version in versions.OrderBy(v => v.VersionNumber))
        {
            if (version.PromptId != id)
                throw new InvalidOperationException($"Version '{version.Id}' belongs to prompt '{version.PromptId}', not '{id}'.");

            if (!seenVersionNumbers.Add(version.VersionNumber))
                throw new InvalidOperationException($"Prompt '{id}' has more than one version numbered {version.VersionNumber}.");

            prompt._versions.Add(version);
        }

        return prompt;
    }

    public PromptVersion CreateVersion(
        string content,
        string createdBy,
        string? changelogEntry = null,
        IReadOnlyList<string>? templateVariables = null,
        DateTimeOffset? createdAt = null)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("Prompt version content is required.", nameof(content));
        if (string.IsNullOrWhiteSpace(createdBy))
            throw new ArgumentException("createdBy is required.", nameof(createdBy));

        var version = PromptVersion.Create(
            promptId: Id,
            versionNumber: _versions.Count + 1,
            content: content,
            createdBy: createdBy,
            parentVersionId: LatestVersion?.Id,
            changelogEntry: changelogEntry,
            templateVariables: templateVariables,
            createdAt: createdAt ?? DateTimeOffset.UtcNow);

        _versions.Add(version);
        return version;
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Prompt name is required.", nameof(name));

        Name = name;
    }

    public void UpdateMetadata(PromptMetadata metadata)
    {
        Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
    }
}
