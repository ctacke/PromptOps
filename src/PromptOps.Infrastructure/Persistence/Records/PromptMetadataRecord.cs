namespace PromptOps.Infrastructure.Persistence.Records;

/// <summary>Mapped to its own table (see <see cref="Configurations.PromptRecordConfiguration"/>) — metadata is stored separately from version content per requirement.</summary>
public sealed class PromptMetadataRecord
{
    public Guid PromptId { get; set; }
    public string Description { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = [];
    public List<string> Categories { get; set; } = [];
    public List<string> Owners { get; set; } = [];
    public List<string> ExternalRefs { get; set; } = [];
}
