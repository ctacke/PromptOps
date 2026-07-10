using Microsoft.Extensions.Options;
using PromptOps.Application.Providers;

namespace PromptOps.Infrastructure.Providers;

/// <summary>
/// <see cref="IAIExecutionProvider"/> (ADR-0003) that shells out to the locally-installed Claude
/// Code CLI in headless/print mode (<c>claude -p</c>), reusing the developer's existing `claude`
/// authentication rather than a separate API key. The prompt is written to the child process's
/// stdin (not passed as a command-line argument) since judge prompts embed a full execution's
/// output and can be arbitrarily large.
/// </summary>
public sealed class ClaudeCliAIExecutionProvider(
    IProcessRunner processRunner,
    IOptions<ClaudeCliOptions> options) : IAIExecutionProvider
{
    public string Name => "claude-cli";

    public async Task<string> ExecuteAsync(
        string promptContent,
        IReadOnlyDictionary<string, string> inputs,
        CancellationToken cancellationToken = default)
    {
        var opts = options.Value;
        var arguments = new List<string> { "-p", "--output-format", "text" };
        if (!string.IsNullOrWhiteSpace(opts.Model))
        {
            arguments.Add("--model");
            arguments.Add(opts.Model);
        }

        var result = await processRunner.RunAsync(opts.ExecutablePath, arguments, promptContent, cancellationToken);

        if (result.ExitCode != 0)
        {
            throw new ClaudeCliExecutionException(result.ExitCode, result.StandardError);
        }

        return result.StandardOutput;
    }
}
