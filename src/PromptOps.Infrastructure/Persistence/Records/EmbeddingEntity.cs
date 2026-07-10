namespace PromptOps.Infrastructure.Persistence.Records;

/// <summary>EF Core persistence shape for one <see cref="PromptOps.Application.Embeddings.IEmbeddingStore"/> entry. Unique on (<see cref="SubjectId"/>, <see cref="SubjectType"/>) — see <see cref="Configurations.EmbeddingEntityConfiguration"/>.</summary>
public sealed class EmbeddingEntity
{
    public Guid Id { get; set; }
    public Guid SubjectId { get; set; }
    public string SubjectType { get; set; } = string.Empty;
    public List<float> Vector { get; set; } = [];
    public DateTimeOffset UpdatedAt { get; set; }
}
