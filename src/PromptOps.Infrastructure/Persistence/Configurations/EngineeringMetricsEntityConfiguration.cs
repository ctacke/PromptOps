using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PromptOps.Infrastructure.Persistence.Records;

namespace PromptOps.Infrastructure.Persistence.Configurations;

public sealed class EngineeringMetricsEntityConfiguration : IEntityTypeConfiguration<EngineeringMetricsEntity>
{
    public void Configure(EntityTypeBuilder<EngineeringMetricsEntity> builder)
    {
        builder.ToTable("EngineeringMetrics");
        builder.HasKey(m => m.Id);
        // Client-generated Guid — see PromptRecordConfiguration for why ValueGeneratedNever matters.
        builder.Property(m => m.Id).ValueGeneratedNever();

        builder.Property(m => m.CollectedBy).IsRequired().HasMaxLength(200);

        // ExecutionId is intentionally a plain value, not a foreign key: EngineeringMetrics is an
        // independent aggregate that references an ExecutionRecord by id only, the same rationale
        // as ExecutionRecord.PromptVersionId (see ExecutionRecordEntityConfiguration).
        builder.Property(m => m.ExecutionId).IsRequired();
        builder.HasIndex(m => m.ExecutionId);
    }
}
