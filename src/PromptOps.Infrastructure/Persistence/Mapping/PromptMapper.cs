using PromptOps.Domain.Prompts;
using PromptOps.Infrastructure.Persistence.Records;

namespace PromptOps.Infrastructure.Persistence.Mapping;

/// <summary>
/// Converts between the <see cref="Prompt"/> aggregate and its EF Core persistence records.
/// <see cref="ApplyChanges"/> reconciles an existing tracked <see cref="PromptRecord"/> against
/// the current state of a domain aggregate rather than replacing it wholesale, so EF Core's
/// change tracker only sees the fields that actually changed.
/// </summary>
internal static class PromptMapper
{
    public static PromptRecord ToNewRecord(Prompt prompt) => new()
    {
        Id = prompt.Id,
        Name = prompt.Name,
        CreatedAt = prompt.CreatedAt,
        Metadata = ToMetadataRecord(prompt.Id, prompt.Metadata),
        Versions = prompt.Versions.Select(ToNewVersionRecord).ToList()
    };

    public static Prompt ToDomain(PromptRecord record)
    {
        var metadata = new PromptMetadata
        {
            Description = record.Metadata.Description,
            Tags = record.Metadata.Tags,
            Categories = record.Metadata.Categories,
            Owners = record.Metadata.Owners,
            ExternalRefs = record.Metadata.ExternalRefs
        };

        var versions = record.Versions.Select(v => PromptVersion.Rehydrate(
            v.Id,
            v.PromptId,
            v.VersionNumber,
            v.Content,
            v.CreatedBy,
            v.ParentVersionId,
            v.ChangelogEntry,
            v.TemplateVariables,
            Enum.Parse<PromptVersionStatus>(v.Status),
            v.CreatedAt,
            v.Dependencies.Select(d => new PromptDependency(
                d.TargetPromptVersionId,
                Enum.Parse<PromptDependencyRelationship>(d.Relationship)))));

        return Prompt.Rehydrate(record.Id, record.Name, metadata, record.CreatedAt, versions);
    }

    public static void ApplyChanges(PromptRecord record, Prompt prompt)
    {
        record.Name = prompt.Name;

        record.Metadata.Description = prompt.Metadata.Description;
        record.Metadata.Tags = prompt.Metadata.Tags.ToList();
        record.Metadata.Categories = prompt.Metadata.Categories.ToList();
        record.Metadata.Owners = prompt.Metadata.Owners.ToList();
        record.Metadata.ExternalRefs = prompt.Metadata.ExternalRefs.ToList();

        foreach (var version in prompt.Versions)
        {
            var existing = record.Versions.FirstOrDefault(v => v.Id == version.Id);
            if (existing is null)
            {
                record.Versions.Add(ToNewVersionRecord(version));
                continue;
            }

            existing.Status = version.Status.ToString();

            var existingDependencyKeys = existing.Dependencies
                .Select(d => (d.TargetPromptVersionId, d.Relationship))
                .ToHashSet();

            foreach (var dependency in version.Dependencies)
            {
                var key = (dependency.TargetPromptVersionId, dependency.Relationship.ToString());
                if (existingDependencyKeys.Contains(key))
                    continue;

                existing.Dependencies.Add(ToNewDependencyRecord(existing.Id, dependency));
            }
        }
    }

    private static PromptMetadataRecord ToMetadataRecord(Guid promptId, PromptMetadata metadata) => new()
    {
        PromptId = promptId,
        Description = metadata.Description,
        Tags = metadata.Tags.ToList(),
        Categories = metadata.Categories.ToList(),
        Owners = metadata.Owners.ToList(),
        ExternalRefs = metadata.ExternalRefs.ToList()
    };

    private static PromptVersionRecord ToNewVersionRecord(PromptVersion version) => new()
    {
        Id = version.Id,
        PromptId = version.PromptId,
        VersionNumber = version.VersionNumber,
        Content = version.Content,
        CreatedBy = version.CreatedBy,
        ParentVersionId = version.ParentVersionId,
        ChangelogEntry = version.ChangelogEntry,
        TemplateVariables = version.TemplateVariables.ToList(),
        Status = version.Status.ToString(),
        CreatedAt = version.CreatedAt,
        Dependencies = version.Dependencies.Select(d => ToNewDependencyRecord(version.Id, d)).ToList()
    };

    private static PromptDependencyRecord ToNewDependencyRecord(Guid versionId, PromptDependency dependency) => new()
    {
        Id = Guid.NewGuid(),
        PromptVersionId = versionId,
        TargetPromptVersionId = dependency.TargetPromptVersionId,
        Relationship = dependency.Relationship.ToString()
    };
}
