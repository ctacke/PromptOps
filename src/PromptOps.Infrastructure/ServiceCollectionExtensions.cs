using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PromptOps.Application.Prompts;
using PromptOps.Infrastructure.Persistence;

namespace PromptOps.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPromptOpsInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("PromptOps") ?? "Data Source=promptops.db";

        services.AddDbContext<PromptOpsDbContext>(options => options.UseSqlite(connectionString));
        services.AddScoped<IPromptRepository, PromptRepository>();

        return services;
    }
}
