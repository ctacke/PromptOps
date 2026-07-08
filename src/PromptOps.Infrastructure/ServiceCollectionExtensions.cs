using Microsoft.Extensions.DependencyInjection;

namespace PromptOps.Infrastructure;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers infrastructure-layer services. EF Core/SQLite repositories and default provider implementations arrive in Phase 2 onward.</summary>
    public static IServiceCollection AddPromptOpsInfrastructure(this IServiceCollection services)
    {
        return services;
    }
}
