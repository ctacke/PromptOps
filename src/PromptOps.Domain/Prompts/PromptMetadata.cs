namespace PromptOps.Domain.Prompts;

/// <summary>
/// Descriptive/taggable data about a <see cref="Prompt"/>, kept separate from version content
/// so metadata can be queried and updated independently of the immutable content history.
/// </summary>
public sealed record PromptMetadata
{
    public string Description { get; init; } = string.Empty;
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Categories { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Owners { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ExternalRefs { get; init; } = Array.Empty<string>();

    public static readonly PromptMetadata Empty = new();
}
