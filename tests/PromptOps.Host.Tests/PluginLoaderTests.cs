using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PromptOps.Application.Executions;
using PromptOps.Application.Providers;
using PromptOps.Domain.Executions;
using PromptOps.Host.Plugins;
using PromptOps.Plugin.Sdk;
using PromptOps.Plugins.BuildResult;
using PromptOps.Plugins.Sonar;
using Xunit;

namespace PromptOps.Host.Tests;

/// <summary>
/// Proves ADR-0004's plugin loading is real: builds a runtime <c>plugins/&lt;Name&gt;/</c>
/// directory layout out of the actual built Sonar/BuildResult plugin assemblies (the same shape
/// the Dockerfile produces), runs <see cref="PluginLoader"/> against it, and resolves the
/// registered <see cref="IMetricCollector"/>s through a real <see cref="IServiceProvider"/>.
/// This is the one place a naive isolated-<see cref="System.Runtime.Loader.AssemblyLoadContext"/>
/// implementation would silently produce type-identity mismatches (see
/// <see cref="PluginLoadContext"/>'s remarks) — asserting on the resolved instances, not just on
/// what <see cref="PluginLoader.LoadAndRegister"/> returns, is what actually catches that class of
/// bug rather than a shallower "no exception was thrown" check.
/// </summary>
public sealed class PluginLoaderTests : IDisposable
{
    private readonly string _pluginsDir = Path.Combine(Path.GetTempPath(), $"promptops-plugin-loader-test-{Guid.NewGuid():N}");

    [Fact]
    public void LoadAndRegister_loads_both_plugins_and_their_collectors_resolve_with_shared_contract_types()
    {
        CopyPluginOutput(typeof(SonarPlugin).Assembly.Location, "PromptOps.Plugins.Sonar");
        CopyPluginOutput(typeof(BuildResultPlugin).Assembly.Location, "PromptOps.Plugins.BuildResult");

        var services = new ServiceCollection();
        services.AddSingleton<IExecutionRepository>(new StubExecutionRepository());
        services.AddSingleton<ISecretProvider>(new StubSecretProvider());
        var configuration = new ConfigurationBuilder().Build();

        var loaded = PluginLoader.LoadAndRegister(services, configuration, _pluginsDir, NullLogger.Instance);

        Assert.Equal(2, loaded.Count);
        Assert.Contains(loaded, p => p.Name == "sonar");
        Assert.Contains(loaded, p => p.Name == "build-result");

        // The real assertion: resolving through DI must not throw (it would, with an
        // InvalidCastException or a "Register does not have an implementation" TypeLoadException
        // at Register-time, if the isolated ALC had loaded its own copy of a shared contract type).
        using var provider = services.BuildServiceProvider();
        var collectors = provider.GetServices<IMetricCollector>().ToList();

        Assert.Equal(2, collectors.Count);
        Assert.Contains(collectors, c => c.Name == "sonar");
        Assert.Contains(collectors, c => c.Name == "build-result");
    }

    [Fact]
    public void LoadAndRegister_returns_empty_when_the_directory_does_not_exist()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        var loaded = PluginLoader.LoadAndRegister(services, configuration, _pluginsDir, NullLogger.Instance);

        Assert.Empty(loaded);
    }

    private void CopyPluginOutput(string builtAssemblyPath, string pluginFolderName)
    {
        var sourceDir = Path.GetDirectoryName(builtAssemblyPath)!;
        var targetDir = Path.Combine(_pluginsDir, pluginFolderName);
        Directory.CreateDirectory(targetDir);

        foreach (var file in Directory.GetFiles(sourceDir, "*.dll").Concat(Directory.GetFiles(sourceDir, "*.deps.json")))
        {
            File.Copy(file, Path.Combine(targetDir, Path.GetFileName(file)), overwrite: true);
        }
    }

    public void Dispose()
    {
        // Best-effort: the plugin DLLs were loaded into a collectible AssemblyLoadContext that
        // nothing here explicitly unloads (the daemon never needs to — plugins live for the
        // process lifetime), so on Windows the files can still be locked at this point.
        try
        {
            if (Directory.Exists(_pluginsDir))
                Directory.Delete(_pluginsDir, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed class StubExecutionRepository : IExecutionRepository
    {
        public Task AddAsync(ExecutionRecord execution, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<ExecutionRecord?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<ExecutionRecord?>(null);
        public Task UpdateAsync(ExecutionRecord execution, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class StubSecretProvider : ISecretProvider
    {
        public Task<string?> GetSecretAsync(string scope, string key, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    }
}
