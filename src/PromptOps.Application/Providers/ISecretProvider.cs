namespace PromptOps.Application.Providers;

/// <summary>
/// Resolves credentials for plugins without the plugin owning secret storage. Secrets are
/// resolvable per scope (e.g. a repository) since different repos' metric-collector plugins
/// may need different credentials (ADR-0007). See ADR-0003.
/// </summary>
public interface ISecretProvider
{
    Task<string?> GetSecretAsync(string scope, string key, CancellationToken cancellationToken = default);
}
