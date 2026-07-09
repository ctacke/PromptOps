using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PromptOps.Infrastructure.Persistence.Records;

namespace PromptOps.Infrastructure.Persistence.Configurations;

public sealed class PromptVersionRecordConfiguration : IEntityTypeConfiguration<PromptVersionRecord>
{
    public void Configure(EntityTypeBuilder<PromptVersionRecord> builder)
    {
        builder.ToTable("PromptVersions");
        builder.HasKey(v => v.Id);
        // Client-generated Guid — see PromptRecordConfiguration for why this matters. Versions
        // are appended to an already-tracked Prompt's Versions collection (not via context.Add()),
        // which is exactly the scenario where the ValueGeneratedOnAdd heuristic gets it wrong.
        builder.Property(v => v.Id).ValueGeneratedNever();
        builder.HasIndex(v => new { v.PromptId, v.VersionNumber }).IsUnique();

        builder.Property(v => v.Content).IsRequired();
        builder.Property(v => v.CreatedBy).IsRequired().HasMaxLength(200);
        builder.Property(v => v.Status).IsRequired().HasMaxLength(50);

        builder.Property(v => v.TemplateVariables)
            .HasConversion(StringListValueConverter.Instance, StringListValueConverter.Comparer);

        builder.HasMany(v => v.Dependencies)
            .WithOne()
            .HasForeignKey(d => d.PromptVersionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
