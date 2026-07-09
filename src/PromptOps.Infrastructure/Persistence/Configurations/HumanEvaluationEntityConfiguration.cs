using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PromptOps.Infrastructure.Persistence.Records;

namespace PromptOps.Infrastructure.Persistence.Configurations;

public sealed class HumanEvaluationEntityConfiguration : IEntityTypeConfiguration<HumanEvaluationEntity>
{
    public void Configure(EntityTypeBuilder<HumanEvaluationEntity> builder)
    {
        builder.ToTable("HumanEvaluations");
        builder.HasKey(e => e.Id);
        // Client-generated Guid — see PromptRecordConfiguration for why ValueGeneratedNever matters.
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.EvaluatorId).IsRequired().HasMaxLength(200);

        // ExecutionId is intentionally a plain value, not a foreign key: HumanEvaluation is an
        // independent aggregate that references an ExecutionRecord by id only, the same rationale
        // as EngineeringMetrics.ExecutionId (see EngineeringMetricsEntityConfiguration).
        builder.Property(e => e.ExecutionId).IsRequired();
        builder.HasIndex(e => e.ExecutionId);
    }
}
