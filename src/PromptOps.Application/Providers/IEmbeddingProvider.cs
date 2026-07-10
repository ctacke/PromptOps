namespace PromptOps.Application.Providers;

/// <summary>
/// Converts text into a fixed-dimension vector for semantic similarity search (Phase 10,
/// ADR-0003). Deliberately not built on <see cref="IAIExecutionProvider"/> — text embedding and
/// chat/completion are different model shapes, unlike the judge (Phase 7) and classifier
/// (Phase 9), which really are just "run a prompt through the same kind of model."
/// </summary>
public interface IEmbeddingProvider
{
    Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default);
}
