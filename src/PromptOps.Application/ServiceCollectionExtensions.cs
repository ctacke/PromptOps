using Microsoft.Extensions.DependencyInjection;
using PromptOps.Application.Prompts;

namespace PromptOps.Application;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPromptOpsApplication(this IServiceCollection services)
    {
        services.AddScoped<PromptService>();
        return services;
    }
}
