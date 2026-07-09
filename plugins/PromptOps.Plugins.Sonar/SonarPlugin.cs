using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PromptOps.Application.Providers;
using PromptOps.Plugin.Sdk;

namespace PromptOps.Plugins.Sonar;

public sealed class SonarPlugin : IPromptOpsPlugin
{
    public string Name => "sonar";
    public string Version => "0.5.0";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SonarOptions>(configuration);
        services.AddHttpClient<SonarMetricCollector>();
        services.AddScoped<IMetricCollector>(sp => sp.GetRequiredService<SonarMetricCollector>());
    }
}
