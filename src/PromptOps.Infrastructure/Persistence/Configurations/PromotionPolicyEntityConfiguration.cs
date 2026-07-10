using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PromptOps.Infrastructure.Persistence.Records;

namespace PromptOps.Infrastructure.Persistence.Configurations;

public sealed class PromotionPolicyEntityConfiguration : IEntityTypeConfiguration<PromotionPolicyEntity>
{
    public void Configure(EntityTypeBuilder<PromotionPolicyEntity> builder)
    {
        builder.ToTable("PromotionPolicies");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
    }
}
