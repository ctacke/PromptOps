using PromptOps.Infrastructure.Providers;
using Xunit;

namespace PromptOps.Infrastructure.Tests;

/// <summary>
/// Exercises the real <see cref="ProcessRunner"/> against the `dotnet` CLI (guaranteed present —
/// it's what's running this test) rather than a shell built-in, so the test works the same on
/// Windows and Linux CI.
/// </summary>
public class ProcessRunnerTests
{
    [Fact]
    public async Task RunAsync_captures_exit_code_and_standard_output()
    {
        var runner = new ProcessRunner();

        var result = await runner.RunAsync("dotnet", ["--version"], standardInput: string.Empty);

        Assert.Equal(0, result.ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(result.StandardOutput));
    }

    [Fact]
    public async Task RunAsync_returns_a_non_zero_exit_code_for_an_invalid_argument()
    {
        var runner = new ProcessRunner();

        var result = await runner.RunAsync("dotnet", ["not-a-real-command"], standardInput: string.Empty);

        Assert.NotEqual(0, result.ExitCode);
    }
}
