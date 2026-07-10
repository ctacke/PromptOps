using Microsoft.EntityFrameworkCore;
using PromptOps.Application.Embeddings;
using PromptOps.Infrastructure.Persistence.Records;

namespace PromptOps.Infrastructure.Persistence;

/// <summary>
/// Default <see cref="IEmbeddingStore"/> (ADR-0005): brute-force cosine similarity over every
/// stored embedding of a given <c>subjectType</c>, loaded into memory and ranked in C# rather than
/// pushed down as a SQL query — architecture.md's own reasoning for why this is fine ("single-machine,
/// single-database scale makes brute-force entirely viable") applies directly here, the same as it
/// does for Phase 9's in-memory tag matching.
/// </summary>
public sealed class EmbeddingStore(PromptOpsDbContext db) : IEmbeddingStore
{
    public async Task StoreAsync(Guid subjectId, string subjectType, float[] embedding, CancellationToken cancellationToken = default)
    {
        var existing = await db.Embeddings
            .FirstOrDefaultAsync(e => e.SubjectId == subjectId && e.SubjectType == subjectType, cancellationToken);

        if (existing is null)
        {
            await db.Embeddings.AddAsync(new EmbeddingEntity
            {
                Id = Guid.NewGuid(),
                SubjectId = subjectId,
                SubjectType = subjectType,
                Vector = embedding.ToList(),
                UpdatedAt = DateTimeOffset.UtcNow
            }, cancellationToken);
        }
        else
        {
            existing.Vector = embedding.ToList();
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<EmbeddingMatch>> FindSimilarAsync(
        float[] queryEmbedding,
        string subjectType,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var candidates = await db.Embeddings
            .AsNoTracking()
            .Where(e => e.SubjectType == subjectType)
            .ToListAsync(cancellationToken);

        return candidates
            .Select(e => new EmbeddingMatch(e.SubjectId, CosineSimilarity(queryEmbedding, e.Vector)))
            .OrderByDescending(m => m.Similarity)
            .Take(limit)
            .ToList();
    }

    private static double CosineSimilarity(float[] a, List<float> b)
    {
        if (a.Length != b.Count) return 0.0;

        double dot = 0, magnitudeA = 0, magnitudeB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            magnitudeA += a[i] * a[i];
            magnitudeB += b[i] * b[i];
        }

        if (magnitudeA <= 0 || magnitudeB <= 0) return 0.0;
        return dot / (Math.Sqrt(magnitudeA) * Math.Sqrt(magnitudeB));
    }
}
