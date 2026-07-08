namespace PromptOps.Domain.Prompts;

/// <summary>
/// A single, immutable version of a <see cref="Prompt"/>'s content. Edits never mutate an
/// existing version — they create a new one with <see cref="ParentVersionId"/> set, forming
/// a lineage chain.
/// </summary>
public sealed class PromptVersion
{
    private readonly List<PromptDependency> _dependencies = [];

    public Guid Id { get; }
    public Guid PromptId { get; }
    public int VersionNumber { get; }
    public string Content { get; }
    public IReadOnlyList<string> TemplateVariables { get; }
    public Guid? ParentVersionId { get; }
    public string? ChangelogEntry { get; }
    public PromptVersionStatus Status { get; private set; }
    public string CreatedBy { get; }
    public DateTimeOffset CreatedAt { get; }
    public IReadOnlyList<PromptDependency> Dependencies => _dependencies.AsReadOnly();

    private PromptVersion(
        Guid id,
        Guid promptId,
        int versionNumber,
        string content,
        string createdBy,
        Guid? parentVersionId,
        string? changelogEntry,
        IReadOnlyList<string> templateVariables,
        PromptVersionStatus status,
        DateTimeOffset createdAt)
    {
        if (versionNumber < 1)
            throw new ArgumentOutOfRangeException(nameof(versionNumber), "Prompt version numbers start at 1.");

        Id = id;
        PromptId = promptId;
        VersionNumber = versionNumber;
        Content = content;
        CreatedBy = createdBy;
        ParentVersionId = parentVersionId;
        ChangelogEntry = changelogEntry;
        TemplateVariables = templateVariables;
        Status = status;
        CreatedAt = createdAt;
    }

    internal static PromptVersion Create(
        Guid promptId,
        int versionNumber,
        string content,
        string createdBy,
        Guid? parentVersionId,
        string? changelogEntry,
        IReadOnlyList<string>? templateVariables,
        DateTimeOffset createdAt)
        => new(
            Guid.NewGuid(),
            promptId,
            versionNumber,
            content,
            createdBy,
            parentVersionId,
            changelogEntry,
            templateVariables ?? [],
            PromptVersionStatus.Draft,
            createdAt);

    /// <summary>Reconstructs a version from persisted state. Not for normal domain use — see <see cref="Prompt.Rehydrate"/>.</summary>
    public static PromptVersion Rehydrate(
        Guid id,
        Guid promptId,
        int versionNumber,
        string content,
        string createdBy,
        Guid? parentVersionId,
        string? changelogEntry,
        IReadOnlyList<string> templateVariables,
        PromptVersionStatus status,
        DateTimeOffset createdAt,
        IEnumerable<PromptDependency> dependencies)
    {
        var version = new PromptVersion(id, promptId, versionNumber, content, createdBy, parentVersionId, changelogEntry, templateVariables, status, createdAt);
        version._dependencies.AddRange(dependencies);
        return version;
    }

    public void Activate()
    {
        if (Status != PromptVersionStatus.Draft)
            throw new InvalidOperationException($"Only a Draft version can be activated (current status: {Status}).");

        Status = PromptVersionStatus.Active;
    }

    public void Deprecate()
    {
        if (Status == PromptVersionStatus.Deprecated)
            throw new InvalidOperationException("Prompt version is already deprecated.");

        Status = PromptVersionStatus.Deprecated;
    }

    public void AddDependency(Guid targetPromptVersionId, PromptDependencyRelationship relationship)
    {
        if (targetPromptVersionId == Id)
            throw new InvalidOperationException("A prompt version cannot depend on itself.");

        if (_dependencies.Any(d => d.TargetPromptVersionId == targetPromptVersionId && d.Relationship == relationship))
            return;

        _dependencies.Add(new PromptDependency(targetPromptVersionId, relationship));
    }
}
