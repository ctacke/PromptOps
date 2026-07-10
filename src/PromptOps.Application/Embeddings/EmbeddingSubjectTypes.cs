namespace PromptOps.Application.Embeddings;

/// <summary>Well-known <c>subjectType</c> values for <see cref="IEmbeddingStore"/>, shared between whoever indexes (e.g. <c>PromptService</c>) and whoever searches (<c>SemanticRecommendationProvider</c>) so both agree on the string without duplicating a magic constant.</summary>
public static class EmbeddingSubjectTypes
{
    public const string PromptVersion = "PromptVersion";
}
