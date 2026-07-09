using Microsoft.EntityFrameworkCore;
using PromptOps.Infrastructure.Persistence.Records;

namespace PromptOps.Infrastructure.Persistence;

public sealed class PromptOpsDbContext(DbContextOptions<PromptOpsDbContext> options) : DbContext(options)
{
    public DbSet<PromptRecord> Prompts => Set<PromptRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PromptOpsDbContext).Assembly);
    }
}
