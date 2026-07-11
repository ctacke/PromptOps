using PromptOps.Application.Prompts;
using PromptOps.Domain.Prompts;

namespace PromptOps.Host.Endpoints;

/// <summary>
/// The <c>/prompts</c> REST surface: creating a Prompt/PromptVersion (the ingestion gap every
/// smoke test from Phase 9 through Phase 11 flagged as out of scope — closed here) plus the manual
/// activation endpoint added in Phase 11.
/// </summary>
public static class PromptEndpoints
{
    public static void MapPromptEndpoints(this WebApplication app)
    {
        app.MapPost("/prompts", async (CreatePromptRequest request, PromptService service, CancellationToken cancellationToken) =>
        {
            try
            {
                var prompt = await service.CreatePromptAsync(request.Name, request.Metadata?.ToDomain(), cancellationToken);
                return Results.Ok(PromptResponse.From(prompt));
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        app.MapGet("/prompts", async (PromptService service, CancellationToken cancellationToken) =>
        {
            var prompts = await service.ListAsync(cancellationToken);
            return Results.Ok(prompts.Select(PromptSummaryResponse.From));
        });

        app.MapGet("/prompts/{promptId:guid}", async (Guid promptId, PromptService service, CancellationToken cancellationToken) =>
        {
            var metadata = await service.GetMetadataAsync(promptId, cancellationToken);
            return metadata is null ? Results.NotFound() : Results.Ok(PromptMetadataResponse.From(metadata));
        });

        app.MapPost("/prompts/{promptId:guid}/versions", async (
            Guid promptId, CreatePromptVersionRequest request, PromptService service, CancellationToken cancellationToken) =>
        {
            try
            {
                var version = await service.CreateVersionAsync(
                    promptId, request.Content, request.CreatedBy, request.ChangelogEntry, request.TemplateVariables, cancellationToken);
                return Results.Ok(PromptVersionResponse.From(version));
            }
            catch (PromptNotFoundException)
            {
                return Results.NotFound();
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        app.MapGet("/prompt-versions/{versionId:guid}", async (Guid versionId, PromptService service, CancellationToken cancellationToken) =>
        {
            var detail = await service.GetVersionDetailAsync(versionId, cancellationToken);
            return detail is null ? Results.NotFound() : Results.Ok(PromptVersionDetailResponse.From(detail));
        });

        app.MapPost("/prompts/{promptId:guid}/versions/{versionId:guid}/activate", async (
            Guid promptId, Guid versionId, PromptService service, CancellationToken cancellationToken) =>
        {
            try
            {
                await service.ActivateVersionAsync(promptId, versionId, cancellationToken);
                return Results.Ok();
            }
            catch (PromptNotFoundException)
            {
                return Results.NotFound();
            }
            catch (PromptVersionNotFoundException)
            {
                return Results.NotFound();
            }
            catch (InvalidOperationException ex)
            {
                // e.g. attempting to reactivate a Deprecated version — see Prompt.ActivateVersion.
                return Results.Conflict(new { error = ex.Message });
            }
        });
    }
}

internal sealed record PromptMetadataDto(
    string? Description = null,
    IReadOnlyList<string>? Tags = null,
    IReadOnlyList<string>? Categories = null,
    IReadOnlyList<string>? Owners = null,
    IReadOnlyList<string>? ExternalRefs = null)
{
    public PromptMetadata ToDomain() => new()
    {
        Description = Description ?? string.Empty,
        Tags = Tags ?? [],
        Categories = Categories ?? [],
        Owners = Owners ?? [],
        ExternalRefs = ExternalRefs ?? []
    };

    public static PromptMetadataDto From(PromptMetadata metadata) => new(
        metadata.Description, metadata.Tags, metadata.Categories, metadata.Owners, metadata.ExternalRefs);
}

internal sealed record CreatePromptRequest(string Name, PromptMetadataDto? Metadata);

internal sealed record CreatePromptVersionRequest(
    string Content, string CreatedBy, string? ChangelogEntry = null, IReadOnlyList<string>? TemplateVariables = null);

internal sealed record PromptResponse(Guid Id, string Name, DateTimeOffset CreatedAt)
{
    public static PromptResponse From(Prompt prompt) => new(prompt.Id, prompt.Name, prompt.CreatedAt);
}

internal sealed record PromptSummaryResponse(Guid Id, string Name)
{
    public static PromptSummaryResponse From(PromptSummary summary) => new(summary.Id, summary.Name);
}

internal sealed record PromptMetadataResponse(Guid Id, string Name, PromptMetadataDto Metadata)
{
    public static PromptMetadataResponse From(PromptMetadataView view) => new(
        view.PromptId, view.Name, PromptMetadataDto.From(view.Metadata));
}

internal sealed record PromptVersionResponse(
    Guid Id, Guid PromptId, int VersionNumber, string Content, string? ChangelogEntry, PromptVersionStatus Status, DateTimeOffset CreatedAt)
{
    public static PromptVersionResponse From(PromptVersion version) => new(
        version.Id, version.PromptId, version.VersionNumber, version.Content, version.ChangelogEntry, version.Status, version.CreatedAt);
}

internal sealed record PromptVersionDetailResponse(
    Guid PromptId, string PromptName, Guid VersionId, int VersionNumber, string Content, PromptVersionStatus Status, IReadOnlyList<string> Tags)
{
    public static PromptVersionDetailResponse From(PromptVersionDetail detail) => new(
        detail.PromptId, detail.PromptName, detail.VersionId, detail.VersionNumber, detail.Content, detail.Status, detail.Tags);
}
