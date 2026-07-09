using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PromptOps.Application.Providers;
using PromptOps.Plugin.Sdk;

namespace PromptOps.Plugins.BuildResult;

public sealed class BuildResultPlugin : IPromptOpsPlugin
{
    public string Name => "build-result";
    public string Version => "0.5.0";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IMetricCollector, BuildResultCollector>();
    }
}
