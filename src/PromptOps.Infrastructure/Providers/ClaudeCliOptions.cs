namespace PromptOps.Infrastructure.Providers;

/// <summary>
/// Configuration for <see cref="ClaudeCliAIExecutionProvider"/>, bound from the "ClaudeCli"
/// configuration section.
/// </summary>
public sealed class ClaudeCliOptions
{
    /// <summary>Executable to invoke — a bare command resolved via PATH by default.</summary>
    public string ExecutablePath { get; set; } = "claude";

    /// <summary>Optional explicit model override; omitted means the CLI's own default.</summary>
    public string? Model { get; set; }
}
