namespace PromptOps.Application.Embeddings;

/// <summary>One result from <see cref="IEmbeddingStore.FindSimilarAsync"/> — a stored subject and its cosine similarity to the query vector.</summary>
public sealed record EmbeddingMatch(Guid SubjectId, double Similarity);
