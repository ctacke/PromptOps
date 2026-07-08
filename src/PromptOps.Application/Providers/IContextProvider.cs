namespace PromptOps.Application.Providers;

/// <summary>
/// Gathers one facet of development context (git, Jira, ADR/doc lookups). Only sources the
/// daemon can reach itself over the network are implemented here (see ADR-0005/§9 of
/// architecture.md) — filesystem-bound facts like git state are pushed by hooks instead.
/// See ADR-0003. First implementations arrive in Phase 3/11+.
/// </summary>
public interface IContextProvider
{
    string Name { get; }

    Task<IReadOnlyDictionary<string, string>> GetContextAsync(CancellationToken cancellationToken = default);
}
