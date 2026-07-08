using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace PromptOps.Plugin.Sdk;

/// <summary>
/// Entry point every PromptOps daemon plugin implements (ADR-0004). Plugins are separate
/// assemblies, loaded from the daemon's plugins directory, that register their provider
/// implementations (<c>IMetricCollector</c>, <c>IContextProvider</c>, etc.) into the daemon's
/// DI container. Discovery/loading is stubbed until Phase 5.
/// </summary>
public interface IPromptOpsPlugin
{
    string Name { get; }

    string Version { get; }

    void Register(IServiceCollection services, IConfiguration configuration);
}
