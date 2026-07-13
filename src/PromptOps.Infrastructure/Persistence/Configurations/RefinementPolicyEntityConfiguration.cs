using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PromptOps.Infrastructure.Persistence.Records;

namespace PromptOps.Infrastructure.Persistence.Configurations;

public sealed class RefinementPolicyEntityConfiguration : IEntityTypeConfiguration<RefinementPolicyEntity>
{
    public void Configure(EntityTypeBuilder<RefinementPolicyEntity> builder)
    {
        builder.ToTable("RefinementPolicies");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
    }
}
