using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PromptOps.Infrastructure.Persistence.Records;

namespace PromptOps.Infrastructure.Persistence.Configurations;

public sealed class ScoringConfigEntityConfiguration : IEntityTypeConfiguration<ScoringConfigEntity>
{
    public void Configure(EntityTypeBuilder<ScoringConfigEntity> builder)
    {
        builder.ToTable("ScoringConfigs");
        builder.HasKey(c => c.Id);
        // Client-generated Guid — see PromptRecordConfiguration for why ValueGeneratedNever matters.
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.Name).IsRequired().HasMaxLength(200);

        // A name can have many versions (immutable — see ScoringConfig's docs), so this is not
        // unique on Name alone; it's unique on the pair, which is what "version" means here.
        builder.HasIndex(c => new { c.Name, c.Version }).IsUnique();
    }
}
