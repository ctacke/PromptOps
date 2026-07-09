using PromptOps.Application.Providers;

namespace PromptOps.Infrastructure.Providers;

/// <summary>
/// Minimal reference <see cref="IAIExecutionProvider"/> — echoes back a manually-supplied output
/// (via an "output" entry in <c>inputs</c>) rather than calling a real AI backend. Proves the
/// execution-recording pipeline works end-to-end without the real Claude Code integration, which
/// is hook-driven and arrives in Phase 4b.
/// </summary>
public sealed class ManualAIExecutionProvider : IAIExecutionProvider
{
    public string Name => "manual";

    public Task<string> ExecuteAsync(
        string promptContent,
        IReadOnlyDictionary<string, string> inputs,
        CancellationToken cancellationToken = default)
    {
        inputs.TryGetValue("output", out var output);
        return Task.FromResult(output ?? string.Empty);
    }
}
