namespace PromptOps.Domain.Executions;

/// <summary>
/// The development context an execution ran in. In production this is assembled by a Claude Code
/// hook (git-derived facts the daemon has no filesystem access to compute itself — ADR-0005 §9)
/// and pushed in; nothing here is fetched by the daemon.
/// </summary>
public sealed record DevelopmentContext
{
    public required string Repository { get; init; }
    public string? Branch { get; init; }
    public string? Commit { get; init; }
    public string? TaskId { get; init; }
    public IReadOnlyList<string> ReferencedDocuments { get; init; } = [];
    public IReadOnlyList<string> ReferencedADRs { get; init; } = [];
    public IReadOnlyList<string> AcceptanceCriteria { get; init; } = [];

    /// <summary>
    /// The repo's dominant language(s) (e.g. "csharp", "typescript"), detected by the hook from
    /// file extensions/manifest files (*.csproj, package.json, pyproject.toml, ...) and pushed
    /// alongside the rest of the context — the daemon never inspects a repo's filesystem itself
    /// (ADR-0005 §9). Not populated until the real hook exists (Phase 4b); empty until then.
    /// </summary>
    public IReadOnlyList<string> Languages { get; init; } = [];
}
