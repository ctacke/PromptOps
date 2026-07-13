using Microsoft.Extensions.DependencyInjection;
using PromptOps.Application.Evaluations;
using PromptOps.Application.Events;
using PromptOps.Application.Executions;
using PromptOps.Application.Metrics;
using PromptOps.Application.Promotion;
using PromptOps.Application.Prompts;
using PromptOps.Application.Recommendations;
using PromptOps.Application.Refinement;
using PromptOps.Application.Scoring;
using PromptOps.Application.Statistics;

namespace PromptOps.Application;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPromptOpsApplication(this IServiceCollection services)
    {
        services.AddScoped<PromptService>();
        services.AddScoped<ExecutionService>();
        services.AddScoped<ExecutionAttributionService>();
        services.AddScoped<MetricsCollectionService>();
        services.AddScoped<HumanEvaluationService>();
        services.AddScoped<AIEvaluationService>();
        services.AddScoped<AIEvaluationPolicyService>();
        services.AddScoped<DelegatedAIEvaluationService>();
        services.AddScoped<ScoringService>();
        services.AddScoped<RecommendationService>();
        services.AddScoped<PromotionPolicyService>();
        services.AddScoped<RefinementPolicyService>();
        services.AddScoped<PromptRefinementService>();
        services.AddScoped<PromptBenchmarkService>();
        services.AddScoped<AbVersionSelector>();
        services.AddScoped<StatisticsService>();
        services.AddScoped<IDomainEventPublisher, DomainEventPublisher>();
        return services;
    }
}
