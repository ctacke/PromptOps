using System.Diagnostics;

namespace PromptOps.Infrastructure.Providers;

/// <summary>
/// Default <see cref="IProcessRunner"/>: writes <c>standardInput</c> to the child process's
/// stdin (so large prompts don't have to fit in a command-line argument), then reads stdout/
/// stderr to completion.
/// </summary>
public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessRunResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string standardInput,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        await process.StandardInput.WriteAsync(standardInput.AsMemory(), cancellationToken);
        process.StandardInput.Close();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        return new ProcessRunResult(process.ExitCode, stdout, stderr);
    }
}
