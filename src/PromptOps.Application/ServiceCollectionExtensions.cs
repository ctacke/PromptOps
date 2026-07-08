using Microsoft.Extensions.DependencyInjection;

namespace PromptOps.Application;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers application-layer services. No use cases are registered yet — they arrive alongside the phases that need them.</summary>
    public static IServiceCollection AddPromptOpsApplication(this IServiceCollection services)
    {
        return services;
    }
}
