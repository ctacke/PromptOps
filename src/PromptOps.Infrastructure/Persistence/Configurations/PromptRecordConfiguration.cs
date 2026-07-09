using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PromptOps.Infrastructure.Persistence.Records;

namespace PromptOps.Infrastructure.Persistence.Configurations;

public sealed class PromptRecordConfiguration : IEntityTypeConfiguration<PromptRecord>
{
    public void Configure(EntityTypeBuilder<PromptRecord> builder)
    {
        builder.ToTable("Prompts");
        builder.HasKey(p => p.Id);
        // Guids are generated client-side (Domain), never by the database — without this, EF's
        // default ValueGeneratedOnAdd heuristic can misclassify a new entity as Modified instead
        // of Added purely because its key is already non-default, producing a failed UPDATE.
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.Name).IsRequired().HasMaxLength(200);
        builder.Property(p => p.CreatedAt).IsRequired();

        // Metadata lives in its own table — metadata must be queryable without touching version content.
        builder.OwnsOne(p => p.Metadata, metadata =>
        {
            metadata.ToTable("PromptMetadata");
            metadata.WithOwner().HasForeignKey(m => m.PromptId);
            metadata.HasKey(m => m.PromptId);

            metadata.Property(m => m.Description).IsRequired().HasMaxLength(2000);

            metadata.Property(m => m.Tags)
                .HasConversion(StringListValueConverter.Instance, StringListValueConverter.Comparer);
            metadata.Property(m => m.Categories)
                .HasConversion(StringListValueConverter.Instance, StringListValueConverter.Comparer);
            metadata.Property(m => m.Owners)
                .HasConversion(StringListValueConverter.Instance, StringListValueConverter.Comparer);
            metadata.Property(m => m.ExternalRefs)
                .HasConversion(StringListValueConverter.Instance, StringListValueConverter.Comparer);
        });

        builder.HasMany(p => p.Versions)
            .WithOne()
            .HasForeignKey(v => v.PromptId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
