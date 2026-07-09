using PromptOps.Application.Providers;

namespace PromptOps.Infrastructure.Providers;

/// <summary>
/// Default <see cref="ISecretProvider"/> (ADR-0003/ADR-0007): reads secrets from environment
/// variables passed into the container, named <c>PROMPTOPS_SECRET_{scope}_{key}</c> (upper-cased,
/// non-alphanumeric characters replaced with <c>_</c>). Keeps plugin credentials out of
/// appsettings/config files that might get checked into a repo.
/// </summary>
public sealed class EnvironmentSecretProvider : ISecretProvider
{
    public Task<string?> GetSecretAsync(string scope, string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var variableName = $"PROMPTOPS_SECRET_{Sanitize(scope)}_{Sanitize(key)}";
        return Task.FromResult(Environment.GetEnvironmentVariable(variableName));
    }

    private static string Sanitize(string value)
    {
        var chars = value.ToUpperInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_');
        return new string(chars.ToArray());
    }
}
