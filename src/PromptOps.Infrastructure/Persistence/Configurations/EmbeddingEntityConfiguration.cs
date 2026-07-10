using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PromptOps.Infrastructure.Persistence.Records;

namespace PromptOps.Infrastructure.Persistence.Configurations;

public sealed class EmbeddingEntityConfiguration : IEntityTypeConfiguration<EmbeddingEntity>
{
    public void Configure(EntityTypeBuilder<EmbeddingEntity> builder)
    {
        builder.ToTable("Embeddings");
        builder.HasKey(e => e.Id);
        // Client-generated Guid — see PromptRecordConfiguration for why ValueGeneratedNever matters.
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.SubjectType).IsRequired().HasMaxLength(100);
        builder.Property(e => e.Vector).HasConversion(FloatListValueConverter.Instance, FloatListValueConverter.Comparer);

        // One embedding per (subject, type) — EmbeddingStore.StoreAsync is an upsert against this.
        builder.HasIndex(e => new { e.SubjectId, e.SubjectType }).IsUnique();
    }
}
