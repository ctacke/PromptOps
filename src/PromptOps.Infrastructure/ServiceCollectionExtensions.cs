using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PromptOps.Application.Executions;
using PromptOps.Application.Metrics;
using PromptOps.Application.Prompts;
using PromptOps.Application.Providers;
using PromptOps.Infrastructure.Persistence;
using PromptOps.Infrastructure.Providers;

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
        services.AddSingleton<IAIExecutionProvider, ManualAIExecutionProvider>();
        services.AddSingleton<ISecretProvider, EnvironmentSecretProvider>();

        return services;
    }
}
