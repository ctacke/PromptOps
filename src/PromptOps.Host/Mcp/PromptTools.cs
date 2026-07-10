using System.ComponentModel;
using ModelContextProtocol.Server;
using PromptOps.Application.Prompts;
using PromptOps.Domain.Prompts;

namespace PromptOps.Host.Mcp;

/// <summary>Creating and reading Prompts/PromptVersions over MCP — the ingestion gap every smoke test from Phase 9 through Phase 11 flagged as out of scope, closed alongside the matching <c>/prompts</c> REST endpoints.</summary>
[McpServerToolType]
public sealed class PromptTools(PromptService service)
{
    [McpServerTool(Name = "create_prompt")]
    [Description("Creates a new Prompt — a named, reusable prompt identity that PromptVersions get added to.")]
    public async Task<object> CreatePrompt(
        [Description("Name of the prompt.")] string name,
        [Description("Optional free-text description.")] string? description = null,
        [Description("Optional tags used for recommendation matching.")] string[]? tags = null,
        CancellationToken cancellationToken = default)
    {
        var metadata = new PromptMetadata { Description = description ?? string.Empty, Tags = tags ?? [] };
        var prompt = await service.CreatePromptAsync(name, metadata, cancellationToken);
        return new { prompt.Id, prompt.Name, prompt.CreatedAt };
    }

    [McpServerTool(Name = "create_prompt_version")]
    [Description("Adds a new content version to an existing Prompt. New versions always start as Draft.")]
    public async Task<object> CreatePromptVersion(
        [Description("The prompt's id.")] Guid promptId,
        [Description("The prompt version's content/text.")] string content,
        [Description("Who's creating this version (e.g. developer email/username).")] string createdBy,
        [Description("Optional changelog entry describing what changed from the prior version.")] string? changelogEntry = null,
        CancellationToken cancellationToken = default)
    {
        var version = await service.CreateVersionAsync(promptId, content, createdBy, changelogEntry, cancellationToken: cancellationToken);
        return new { version.Id, version.PromptId, version.VersionNumber, Status = version.Status.ToString() };
    }

    [McpServerTool(Name = "list_prompts")]
    [Description("Lists every Prompt's id and name in the shared database — no metadata, no version content. Used to check whether a prompt with a given name already exists before creating one (e.g. /promptops init's de-dup check).")]
    public async Task<object> ListPrompts(CancellationToken cancellationToken = default)
    {
        var prompts = await service.ListAsync(cancellationToken);
        return prompts.Select(p => new { p.Id, p.Name });
    }

    [McpServerTool(Name = "get_prompt")]
    [Description("Gets a Prompt's metadata (name, description, tags) without loading its version content.")]
    public async Task<object?> GetPrompt(
        [Description("The prompt's id.")] Guid promptId,
        CancellationToken cancellationToken = default)
    {
        var metadata = await service.GetMetadataAsync(promptId, cancellationToken);
        if (metadata is null)
            return null;

        return new { metadata.PromptId, metadata.Name, metadata.Metadata.Description, metadata.Metadata.Tags };
    }
}
