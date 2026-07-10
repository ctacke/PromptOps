namespace PromptOps.Infrastructure.Providers;

public sealed class ClaudeCliExecutionException(int exitCode, string standardError)
    : Exception($"claude CLI exited with code {exitCode}: {standardError}")
{
    public int ExitCode { get; } = exitCode;
}
