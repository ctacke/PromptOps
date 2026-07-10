using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PromptOps.Application.Providers;
using PromptOps.Infrastructure.Providers;
using Xunit;

namespace PromptOps.Infrastructure.Tests;

/// <summary>
/// Confirms <see cref="ServiceCollectionExtensions.AddPromptOpsInfrastructure"/> picks the judge
/// backend via the "AIExecution:Provider" config key, and defaults to the manual/echo provider
/// so a fresh clone (and every test that drives judge responses through
/// <c>parameters["output"]</c>) behaves exactly as before.
/// </summary>
public class AIExecutionProviderSelectionTests
{
    private static IAIExecutionProvider Resolve(string? providerSetting)
    {
        var configValues = new Dictionary<string, string?>
        {
            ["ConnectionStrings:PromptOps"] = "Data Source=:memory:",
        };
        if (providerSetting is not null)
        {
            configValues["AIExecution:Provider"] = providerSetting;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(configValues).Build();
        var services = new ServiceCollection().AddPromptOpsInfrastructure(configuration);

        return services.BuildServiceProvider().GetRequiredService<IAIExecutionProvider>();
    }

    [Fact]
    public void Defaults_to_the_manual_provider_when_unconfigured()
    {
        var provider = Resolve(providerSetting: null);

        Assert.IsType<ManualAIExecutionProvider>(provider);
    }

    [Fact]
    public void Uses_the_manual_provider_when_explicitly_configured()
    {
        var provider = Resolve("manual");

        Assert.IsType<ManualAIExecutionProvider>(provider);
    }

    [Fact]
    public void Uses_the_claude_cli_provider_when_configured()
    {
        var provider = Resolve("claude-cli");

        Assert.IsType<ClaudeCliAIExecutionProvider>(provider);
    }
}
