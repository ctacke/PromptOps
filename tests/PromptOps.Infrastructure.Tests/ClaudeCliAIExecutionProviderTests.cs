using Microsoft.Extensions.Options;
using PromptOps.Infrastructure.Providers;
using Xunit;

namespace PromptOps.Infrastructure.Tests;

public class ClaudeCliAIExecutionProviderTests
{
    [Fact]
    public async Task Returns_standard_output_on_a_zero_exit_code()
    {
        var runner = new FakeProcessRunner(new ProcessRunResult(0, "the judge's response", string.Empty));
        var provider = new ClaudeCliAIExecutionProvider(runner, Options.Create(new ClaudeCliOptions()));

        var result = await provider.ExecuteAsync("judge this", new Dictionary<string, string>());

        Assert.Equal("the judge's response", result);
    }

    [Fact]
    public async Task Writes_the_prompt_to_stdin_rather_than_as_an_argument()
    {
        var runner = new FakeProcessRunner(new ProcessRunResult(0, "ok", string.Empty));
        var provider = new ClaudeCliAIExecutionProvider(runner, Options.Create(new ClaudeCliOptions()));

        await provider.ExecuteAsync("a very long judge prompt", new Dictionary<string, string>());

        Assert.Equal("a very long judge prompt", runner.LastStandardInput);
        Assert.DoesNotContain(runner.LastArguments!, a => a.Contains("a very long judge prompt"));
    }

    [Fact]
    public async Task Uses_the_configured_executable_and_model()
    {
        var runner = new FakeProcessRunner(new ProcessRunResult(0, "ok", string.Empty));
        var options = Options.Create(new ClaudeCliOptions { ExecutablePath = "/usr/local/bin/claude", Model = "claude-opus-4-8" });
        var provider = new ClaudeCliAIExecutionProvider(runner, options);

        await provider.ExecuteAsync("judge this", new Dictionary<string, string>());

        Assert.Equal("/usr/local/bin/claude", runner.LastFileName);
        Assert.Contains("--model", runner.LastArguments!);
        Assert.Contains("claude-opus-4-8", runner.LastArguments!);
    }

    [Fact]
    public async Task Throws_with_stderr_when_the_process_exits_non_zero()
    {
        var runner = new FakeProcessRunner(new ProcessRunResult(1, string.Empty, "not logged in"));
        var provider = new ClaudeCliAIExecutionProvider(runner, Options.Create(new ClaudeCliOptions()));

        var ex = await Assert.ThrowsAsync<ClaudeCliExecutionException>(
            () => provider.ExecuteAsync("judge this", new Dictionary<string, string>()));

        Assert.Equal(1, ex.ExitCode);
        Assert.Contains("not logged in", ex.Message);
    }

    private sealed class FakeProcessRunner(ProcessRunResult result) : IProcessRunner
    {
        public string? LastFileName { get; private set; }
        public IReadOnlyList<string>? LastArguments { get; private set; }
        public string? LastStandardInput { get; private set; }

        public Task<ProcessRunResult> RunAsync(
            string fileName, IReadOnlyList<string> arguments, string standardInput, CancellationToken cancellationToken = default)
        {
            LastFileName = fileName;
            LastArguments = arguments;
            LastStandardInput = standardInput;
            return Task.FromResult(result);
        }
    }
}
