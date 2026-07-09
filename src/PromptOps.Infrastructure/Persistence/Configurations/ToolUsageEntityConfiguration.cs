using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PromptOps.Infrastructure.Persistence.Records;

namespace PromptOps.Infrastructure.Persistence.Configurations;

public sealed class ToolUsageEntityConfiguration : IEntityTypeConfiguration<ToolUsageEntity>
{
    public void Configure(EntityTypeBuilder<ToolUsageEntity> builder)
    {
        builder.ToTable("ExecutionToolUsages");
        builder.HasKey(t => t.Id);
        // Client-generated Guid — see PromptRecordConfiguration for why this matters.
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.Name).IsRequired().HasMaxLength(200);
    }
}
