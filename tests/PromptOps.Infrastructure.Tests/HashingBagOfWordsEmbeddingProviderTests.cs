using PromptOps.Infrastructure.Providers;
using Xunit;

namespace PromptOps.Infrastructure.Tests;

public class HashingBagOfWordsEmbeddingProviderTests
{
    private static double CosineSimilarity(float[] a, float[] b)
    {
        double dot = 0, magnitudeA = 0, magnitudeB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            magnitudeA += a[i] * a[i];
            magnitudeB += b[i] * b[i];
        }
        return dot / (Math.Sqrt(magnitudeA) * Math.Sqrt(magnitudeB));
    }

    [Fact]
    public async Task Produces_A_Vector_Of_The_Declared_Dimension()
    {
        var provider = new HashingBagOfWordsEmbeddingProvider();

        var embedding = await provider.EmbedAsync("hello world");

        Assert.Equal(HashingBagOfWordsEmbeddingProvider.Dimensions, embedding.Length);
    }

    [Fact]
    public async Task Is_Deterministic_Across_Separate_Provider_Instances()
    {
        // Simulates two different daemon process runs — critical, since .NET's built-in
        // string.GetHashCode() is randomized per process and would break this.
        var first = await new HashingBagOfWordsEmbeddingProvider().EmbedAsync("null reference exception in login flow");
        var second = await new HashingBagOfWordsEmbeddingProvider().EmbedAsync("null reference exception in login flow");

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Returns_A_Unit_Vector_For_Nonempty_Text()
    {
        var provider = new HashingBagOfWordsEmbeddingProvider();

        var embedding = await provider.EmbedAsync("some text with several distinct words here");
        var magnitude = Math.Sqrt(embedding.Sum(v => v * v));

        Assert.Equal(1.0, magnitude, precision: 5);
    }

    [Fact]
    public async Task Returns_The_Zero_Vector_For_Empty_Text_Without_Throwing()
    {
        var provider = new HashingBagOfWordsEmbeddingProvider();

        var embedding = await provider.EmbedAsync("");

        Assert.All(embedding, v => Assert.Equal(0f, v));
    }

    [Fact]
    public async Task Texts_Sharing_Vocabulary_Are_More_Similar_Than_Texts_That_Dont()
    {
        var provider = new HashingBagOfWordsEmbeddingProvider();

        var a = await provider.EmbedAsync("null reference exception debugging the login flow");
        var b = await provider.EmbedAsync("debugging a null reference exception in authentication");
        var c = await provider.EmbedAsync("write release notes summarizing recent commits");

        var similarAB = CosineSimilarity(a, b);
        var dissimilarAC = CosineSimilarity(a, c);

        Assert.True(similarAB > dissimilarAC, $"expected shared-vocabulary similarity ({similarAB}) to exceed unrelated similarity ({dissimilarAC})");
    }

    [Fact]
    public async Task Is_Case_Insensitive()
    {
        var provider = new HashingBagOfWordsEmbeddingProvider();

        var lower = await provider.EmbedAsync("Null Reference Exception");
        var upper = await provider.EmbedAsync("null reference exception");

        Assert.Equal(lower, upper);
    }
}
