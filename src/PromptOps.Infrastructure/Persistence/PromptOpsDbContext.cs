using Microsoft.EntityFrameworkCore;
using PromptOps.Infrastructure.Persistence.Records;

namespace PromptOps.Infrastructure.Persistence;

public sealed class PromptOpsDbContext(DbContextOptions<PromptOpsDbContext> options) : DbContext(options)
{
    public DbSet<PromptRecord> Prompts => Set<PromptRecord>();
    public DbSet<ExecutionRecordEntity> Executions => Set<ExecutionRecordEntity>();
    public DbSet<EngineeringMetricsEntity> EngineeringMetrics => Set<EngineeringMetricsEntity>();
    public DbSet<HumanEvaluationEntity> HumanEvaluations => Set<HumanEvaluationEntity>();
    public DbSet<AIEvaluationEntity> AIEvaluations => Set<AIEvaluationEntity>();
    public DbSet<ScoringConfigEntity> ScoringConfigs => Set<ScoringConfigEntity>();
    public DbSet<PromptScoreEntity> PromptScores => Set<PromptScoreEntity>();
    public DbSet<EmbeddingEntity> Embeddings => Set<EmbeddingEntity>();
    public DbSet<PromotionPolicyEntity> PromotionPolicies => Set<PromotionPolicyEntity>();
    public DbSet<AIEvaluationPolicyEntity> AIEvaluationPolicies => Set<AIEvaluationPolicyEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PromptOpsDbContext).Assembly);
    }
}
