using System.Text.RegularExpressions;
using PromptOps.Application.Providers;

namespace PromptOps.Infrastructure.Providers;

/// <summary>
/// Reference <see cref="IEmbeddingProvider"/> (Phase 10): a deterministic, local, no-API-key
/// feature-hashing (a.k.a. "hashing trick") bag-of-words embedding — not a real semantic/transformer
/// embedding model. Same story as <c>ManualAIExecutionProvider</c> (Phase 3): this proves the
/// indexing/similarity-search pipeline end-to-end without requiring a network call or a heavy
/// local model; a real embedding-model-backed <see cref="IEmbeddingProvider"/> is a natural future
/// plugin that swaps in underneath <see cref="SemanticRecommendationProvider"/> with no other
/// change, the same way a real <c>IAIExecutionProvider</c> would slot under the judge/classifier.
///
/// It still produces a genuine similarity signal for shared/overlapping vocabulary — two texts
/// that use similar words get a higher cosine similarity than two that don't — which is exactly
/// what Phase 10's acceptance criterion needs for tag-less matches like "NullReferenceException"
/// vs. "null reference exception".
///
/// <b>Determinism matters here</b>: .NET's built-in <see cref="string.GetHashCode()"/> is
/// randomized per process by design (a security mitigation), so two runs of the daemon would hash
/// the same word to different buckets and produce embeddings that aren't comparable across a
/// restart. <see cref="Fnv1a"/> is a plain, unseeded, stable hash chosen specifically to avoid that.
/// </summary>
public sealed class HashingBagOfWordsEmbeddingProvider : IEmbeddingProvider
{
    public const int Dimensions = 128;

    private static readonly Regex TokenPattern = new(@"[a-z0-9]+", RegexOptions.Compiled);

    public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        var vector = new float[Dimensions];

        foreach (Match match in TokenPattern.Matches(text.ToLowerInvariant()))
        {
            var bucket = (int)(Fnv1a(match.Value) % Dimensions);
            vector[bucket] += 1f;
        }

        Normalize(vector);
        return Task.FromResult(vector);
    }

    private static void Normalize(float[] vector)
    {
        var magnitude = MathF.Sqrt(vector.Sum(v => v * v));
        if (magnitude <= 0f) return;

        for (var i = 0; i < vector.Length; i++)
            vector[i] /= magnitude;
    }

    /// <summary>32-bit FNV-1a — simple, stable across processes/.NET versions, adequate for bucket assignment (not cryptographic).</summary>
    private static uint Fnv1a(string token)
    {
        const uint offsetBasis = 2166136261;
        const uint prime = 16777619;

        var hash = offsetBasis;
        foreach (var c in token)
        {
            hash ^= c;
            hash *= prime;
        }
        return hash;
    }
}
