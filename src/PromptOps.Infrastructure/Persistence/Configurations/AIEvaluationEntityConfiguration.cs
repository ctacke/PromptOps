using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PromptOps.Infrastructure.Persistence.Records;

namespace PromptOps.Infrastructure.Persistence.Configurations;

public sealed class AIEvaluationEntityConfiguration : IEntityTypeConfiguration<AIEvaluationEntity>
{
    public void Configure(EntityTypeBuilder<AIEvaluationEntity> builder)
    {
        builder.ToTable("AIEvaluations");
        builder.HasKey(e => e.Id);
        // Client-generated Guid — see PromptRecordConfiguration for why ValueGeneratedNever matters.
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.JudgeProviderId).IsRequired().HasMaxLength(200);
        builder.Property(e => e.RawResponse).IsRequired();

        builder.Property(e => e.AdrViolations)
            .HasConversion(StringListValueConverter.Instance, StringListValueConverter.Comparer);
        builder.Property(e => e.IgnoredRequirements)
            .HasConversion(StringListValueConverter.Instance, StringListValueConverter.Comparer);
        builder.Property(e => e.SuggestedPromptImprovements)
            .HasConversion(StringListValueConverter.Instance, StringListValueConverter.Comparer);

        // ExecutionId is intentionally a plain value, not a foreign key: AIEvaluation is an
        // independent aggregate that references an ExecutionRecord by id only, the same rationale
        // as HumanEvaluation.ExecutionId (see HumanEvaluationEntityConfiguration).
        builder.Property(e => e.ExecutionId).IsRequired();
        builder.HasIndex(e => e.ExecutionId);
    }
}
