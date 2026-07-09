using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PromptOps.Infrastructure.Persistence.Records;

namespace PromptOps.Infrastructure.Persistence.Configurations;

public sealed class PromptScoreEntityConfiguration : IEntityTypeConfiguration<PromptScoreEntity>
{
    public void Configure(EntityTypeBuilder<PromptScoreEntity> builder)
    {
        builder.ToTable("PromptScores");
        builder.HasKey(s => s.Id);
        // Client-generated Guid — see PromptRecordConfiguration for why ValueGeneratedNever matters.
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.ComponentScores)
            .HasConversion(StringDoubleDictionaryValueConverter.Instance, StringDoubleDictionaryValueConverter.Comparer);

        // PromptVersionId/ScoringConfigId are intentionally plain values, not foreign keys —
        // PromptScore is an independent aggregate referencing both by id only, same rationale as
        // EngineeringMetrics.ExecutionId (see EngineeringMetricsEntityConfiguration). ScoringConfigId
        // in particular must never cascade-delete: an old score has to keep meaning what it meant
        // even if its config were ever removed.
        builder.Property(s => s.PromptVersionId).IsRequired();
        builder.Property(s => s.ScoringConfigId).IsRequired();
        builder.HasIndex(s => s.PromptVersionId);
    }
}
