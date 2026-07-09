using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PromptOps.Infrastructure.Persistence.Records;

namespace PromptOps.Infrastructure.Persistence.Configurations;

public sealed class ExecutionRecordEntityConfiguration : IEntityTypeConfiguration<ExecutionRecordEntity>
{
    public void Configure(EntityTypeBuilder<ExecutionRecordEntity> builder)
    {
        builder.ToTable("Executions");
        builder.HasKey(e => e.Id);
        // Client-generated Guid — see PromptRecordConfiguration for why ValueGeneratedNever matters.
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.DeveloperId).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Repository).IsRequired().HasMaxLength(500);
        builder.Property(e => e.Status).IsRequired().HasMaxLength(50);

        builder.Property(e => e.ReferencedDocuments)
            .HasConversion(StringListValueConverter.Instance, StringListValueConverter.Comparer);
        builder.Property(e => e.ReferencedADRs)
            .HasConversion(StringListValueConverter.Instance, StringListValueConverter.Comparer);
        builder.Property(e => e.AcceptanceCriteria)
            .HasConversion(StringListValueConverter.Instance, StringListValueConverter.Comparer);
        builder.Property(e => e.Languages)
            .HasConversion(StringListValueConverter.Instance, StringListValueConverter.Comparer);
        builder.Property(e => e.FilesChanged)
            .HasConversion(StringListValueConverter.Instance, StringListValueConverter.Comparer);
        builder.Property(e => e.Inputs)
            .HasConversion(StringDictionaryValueConverter.Instance, StringDictionaryValueConverter.Comparer);

        builder.HasMany(e => e.ToolUsage)
            .WithOne()
            .HasForeignKey(t => t.ExecutionId)
            .OnDelete(DeleteBehavior.Cascade);

        // PromptVersionId is intentionally a plain value, not a foreign key: ExecutionRecord is an
        // independent aggregate that references a PromptVersion by id only, the same rationale as
        // PromptDependency.TargetPromptVersionId (see PromptRecordConfiguration).
        builder.Property(e => e.PromptVersionId).IsRequired();
    }
}
