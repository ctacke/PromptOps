using Microsoft.EntityFrameworkCore;
using PromptOps.Infrastructure.Persistence.Records;

namespace PromptOps.Infrastructure.Persistence;

public sealed class PromptOpsDbContext(DbContextOptions<PromptOpsDbContext> options) : DbContext(options)
{
    public DbSet<PromptRecord> Prompts => Set<PromptRecord>();
    public DbSet<ExecutionRecordEntity> Executions => Set<ExecutionRecordEntity>();
    public DbSet<EngineeringMetricsEntity> EngineeringMetrics => Set<EngineeringMetricsEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PromptOpsDbContext).Assembly);
    }
}
