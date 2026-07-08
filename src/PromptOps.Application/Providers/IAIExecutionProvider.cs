namespace PromptOps.Application.Providers;

/// <summary>
/// Executes a resolved prompt against a specific AI backend (Claude Code, ChatGPT, Copilot,
/// a local model, ...). See ADR-0003. Concrete implementations are hook/plugin-driven,
/// starting in Phase 4b.
/// </summary>
public interface IAIExecutionProvider
{
    string Name { get; }

    Task<string> ExecuteAsync(
        string promptContent,
        IReadOnlyDictionary<string, string> inputs,
        CancellationToken cancellationToken = default);
}
