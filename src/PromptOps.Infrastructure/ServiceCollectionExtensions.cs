using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PromptOps.Application.Embeddings;
using PromptOps.Application.Evaluations;
using PromptOps.Application.Events;
using PromptOps.Application.Executions;
using PromptOps.Application.Metrics;
using PromptOps.Application.Promotion;
using PromptOps.Application.Prompts;
using PromptOps.Application.Providers;
using PromptOps.Application.Scoring;
using PromptOps.Domain.Evaluations;
using PromptOps.Domain.Executions;
using PromptOps.Domain.Metrics;
using PromptOps.Domain.Scoring;
using PromptOps.Infrastructure.Persistence;
using PromptOps.Infrastructure.Promotion;
using PromptOps.Infrastructure.Providers;
using PromptOps.Infrastructure.Scoring;

namespace PromptOps.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPromptOpsInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("PromptOps") ?? "Data Source=promptops.db";

        services.AddDbContext<PromptOpsDbContext>(options => options.UseSqlite(connectionString));
        services.AddScoped<IPromptRepository, PromptRepository>();
        services.AddScoped<IExecutionRepository, ExecutionRepository>();
        services.AddScoped<IEngineeringMetricsRepository, EngineeringMetricsRepository>();
        services.AddScoped<IHumanEvaluationRepository, HumanEvaluationRepository>();
        services.AddScoped<IAIEvaluationRepository, AIEvaluationRepository>();
        services.AddScoped<IScoringConfigRepository, ScoringConfigRepository>();
        services.AddScoped<IPromptScoreRepository, PromptScoreRepository>();
        services.AddSingleton<IAIExecutionProvider, ManualAIExecutionProvider>();
        services.AddScoped<IAIEvaluationProvider, AIJudgeEvaluationProvider>();
        services.AddScoped<IScoringProvider, WeightedSumScoringProvider>();
        services.AddScoped<IActivityClassifier, AIActivityClassifier>();
        // v1 stays registered as a concrete type (directly testable/usable on its own); v2 is the
        // bound IRecommendationProvider as of Phase 10 — see SemanticRecommendationProvider's docs.
        services.AddScoped<TagAndHistoryRecommendationProvider>();
        services.AddScoped<IRecommendationProvider, SemanticRecommendationProvider>();
        services.AddSingleton<IEmbeddingProvider, HashingBagOfWordsEmbeddingProvider>();
        services.AddScoped<IEmbeddingStore, EmbeddingStore>();
        services.AddSingleton<ISecretProvider, EnvironmentSecretProvider>();
        services.AddScoped<IPromotionPolicyRepository, PromotionPolicyRepository>();

        // Recompute-on-event (debounced, Phase 8): one singleton scheduler, four domain event
        // registrations feeding it (see ScoreRecomputeTrigger's docs for why one class/four regs).
        services.AddSingleton<IScoreRecomputeScheduler, DebouncedScoreRecomputeScheduler>();
        services.AddScoped<IDomainEventHandler<ExecutionRecorded>, ScoreRecomputeTrigger>();
        services.AddScoped<IDomainEventHandler<MetricsCollected>, ScoreRecomputeTrigger>();
        services.AddScoped<IDomainEventHandler<HumanEvaluationSubmitted>, ScoreRecomputeTrigger>();
        services.AddScoped<IDomainEventHandler<AIEvaluationRecorded>, ScoreRecomputeTrigger>();

        // Auto-promotion (Phase 11): the first-ever handler for ScoreComputed.
        services.AddScoped<IDomainEventHandler<ScoreComputed>, AutoPromotionTrigger>();

        return services;
    }
}
