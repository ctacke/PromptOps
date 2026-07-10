using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PromptOps.Infrastructure.Persistence.Records;

namespace PromptOps.Infrastructure.Persistence.Configurations;

public sealed class AIEvaluationPolicyEntityConfiguration : IEntityTypeConfiguration<AIEvaluationPolicyEntity>
{
    public void Configure(EntityTypeBuilder<AIEvaluationPolicyEntity> builder)
    {
        builder.ToTable("AIEvaluationPolicies");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
    }
}
