using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PromptOps.Infrastructure.Persistence.Records;

namespace PromptOps.Infrastructure.Persistence.Configurations;

public sealed class RefinementCandidateEntityConfiguration : IEntityTypeConfiguration<RefinementCandidateEntity>
{
    public void Configure(EntityTypeBuilder<RefinementCandidateEntity> builder)
    {
        builder.ToTable("RefinementCandidates");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Status).IsRequired().HasMaxLength(50);
        builder.HasIndex(c => c.DraftVersionId);
        builder.HasIndex(c => c.ActiveVersionId);
    }
}
