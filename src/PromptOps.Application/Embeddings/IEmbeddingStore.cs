namespace PromptOps.Application.Embeddings;

/// <summary>
/// In-process similarity index (ADR-0005: "brute-force cosine over stored embeddings —
/// single-machine, single-database scale makes brute-force entirely viable", named
/// <c>IEmbeddingStore</c> there explicitly). One embedding per (subjectId, subjectType) —
/// <see cref="StoreAsync"/> is an upsert, since a <c>PromptVersion</c>'s embedded text (content +
/// tags + description) can change (re-tagging) after it was first indexed.
/// </summary>
public interface IEmbeddingStore
{
    Task StoreAsync(Guid subjectId, string subjectType, float[] embedding, CancellationToken cancellationToken = default);

    /// <summary>Ranked by cosine similarity, descending, restricted to <paramref name="subjectType"/>.</summary>
    Task<IReadOnlyList<EmbeddingMatch>> FindSimilarAsync(
        float[] queryEmbedding,
        string subjectType,
        int limit,
        CancellationToken cancellationToken = default);
}
