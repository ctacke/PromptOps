using Microsoft.Extensions.DependencyInjection;
using PromptOps.Application.Events;
using PromptOps.Application.Executions;
using PromptOps.Application.Prompts;

namespace PromptOps.Application;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPromptOpsApplication(this IServiceCollection services)
    {
        services.AddScoped<PromptService>();
        services.AddScoped<ExecutionService>();
        services.AddScoped<IDomainEventPublisher, DomainEventPublisher>();
        return services;
    }
}
