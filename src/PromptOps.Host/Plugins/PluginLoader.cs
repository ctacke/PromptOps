using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PromptOps.Plugin.Sdk;

namespace PromptOps.Host.Plugins;

/// <summary>
/// Makes ADR-0004's plugin discovery real: scans <paramref name="pluginsDirectory"/> for
/// subdirectories, loads each one's primary assembly into its own <see cref="PluginLoadContext"/>,
/// finds <see cref="IPromptOpsPlugin"/> implementations, and calls <c>Register</c> with the
/// configuration section scoped to that plugin's name (<c>Plugins:{plugin.Name}</c>).
///
/// Convention over manifest: a plugin's primary DLL is expected at
/// <c>{pluginsDirectory}/{FolderName}/{FolderName}.dll</c> (exactly what
/// <c>dotnet publish -o {pluginsDirectory}/{FolderName}</c> produces) rather than a separate
/// manifest file declaring it — there was nothing a manifest would tell us here that the folder
/// layout doesn't already.
/// </summary>
public static class PluginLoader
{
    public static IReadOnlyList<IPromptOpsPlugin> LoadAndRegister(
        IServiceCollection services,
        IConfiguration configuration,
        string pluginsDirectory,
        ILogger logger)
    {
        var loaded = new List<IPromptOpsPlugin>();

        if (!Directory.Exists(pluginsDirectory))
        {
            logger.LogInformation("No plugins directory at {Path} — starting with zero plugins.", pluginsDirectory);
            return loaded;
        }

        foreach (var pluginDir in Directory.GetDirectories(pluginsDirectory))
        {
            var folderName = Path.GetFileName(pluginDir);
            var dllPath = Path.Combine(pluginDir, $"{folderName}.dll");

            if (!File.Exists(dllPath))
            {
                logger.LogWarning("Skipping plugin directory {Directory}: expected {DllPath}.", pluginDir, dllPath);
                continue;
            }

            try
            {
                var loadContext = new PluginLoadContext(dllPath);
                var assembly = loadContext.LoadFromAssemblyPath(dllPath);

                var pluginTypes = assembly.GetTypes()
                    .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IPromptOpsPlugin).IsAssignableFrom(t));

                foreach (var pluginType in pluginTypes)
                {
                    var plugin = (IPromptOpsPlugin)Activator.CreateInstance(pluginType)!;
                    plugin.Register(services, configuration.GetSection($"Plugins:{plugin.Name}"));
                    loaded.Add(plugin);
                    logger.LogInformation("Loaded plugin '{Name}' v{Version} from {DllPath}.", plugin.Name, plugin.Version, dllPath);
                }
            }
            catch (Exception ex)
            {
                // A broken plugin must not take down the daemon (ADR-0004) — log and move on.
                logger.LogError(ex, "Failed to load plugin from {DllPath}.", dllPath);
            }
        }

        return loaded;
    }
}
