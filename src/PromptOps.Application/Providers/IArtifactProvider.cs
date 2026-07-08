namespace PromptOps.Application.Providers;

/// <summary>
/// Persists/retrieves large artifacts (execution transcripts, diffs, logs) outside the
/// relational store. See ADR-0003 and ADR-0005 (default implementation writes to the daemon's
/// Docker volume, alongside the SQLite database).
/// </summary>
public interface IArtifactProvider
{
    Task<string> StoreAsync(string key, Stream content, CancellationToken cancellationToken = default);

    Task<Stream> RetrieveAsync(string key, CancellationToken cancellationToken = default);
}
