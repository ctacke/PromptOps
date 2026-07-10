namespace PromptOps.Infrastructure.Providers;

/// <summary>
/// Thin seam over <see cref="System.Diagnostics.Process"/> so anything that shells out to an
/// external CLI (e.g. <see cref="ClaudeCliAIExecutionProvider"/>) can be unit-tested against a
/// fake instead of actually spawning a process.
/// </summary>
public interface IProcessRunner
{
    Task<ProcessRunResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string standardInput,
        CancellationToken cancellationToken = default);
}

public sealed record ProcessRunResult(int ExitCode, string StandardOutput, string StandardError);
