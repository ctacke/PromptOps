using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PromptOps.Infrastructure.Persistence.Records;

namespace PromptOps.Infrastructure.Persistence.Configurations;

public sealed class PromptDependencyRecordConfiguration : IEntityTypeConfiguration<PromptDependencyRecord>
{
    public void Configure(EntityTypeBuilder<PromptDependencyRecord> builder)
    {
        builder.ToTable("PromptDependencies");
        builder.HasKey(d => d.Id);
        // Client-generated Guid — see PromptRecordConfiguration for why this matters.
        builder.Property(d => d.Id).ValueGeneratedNever();
        builder.Property(d => d.Relationship).IsRequired().HasMaxLength(50);

        // TargetPromptVersionId is a plain value, not a navigation/FK — the target may belong
        // to a different Prompt aggregate (cross-prompt dependencies are expected).
        builder.Property(d => d.TargetPromptVersionId).IsRequired();
    }
}
