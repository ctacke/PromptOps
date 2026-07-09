using System.Reflection;
using System.Runtime.Loader;

namespace PromptOps.Host.Plugins;

/// <summary>
/// Isolated <see cref="AssemblyLoadContext"/> per plugin (ADR-0004) — a broken third-party plugin
/// can't take down the host process, and one plugin's private dependency version can't collide
/// with another's.
///
/// The one thing that must NOT be isolated is the contract surface every plugin is built against:
/// <c>PromptOps.Domain</c>/<c>PromptOps.Application</c>/<c>PromptOps.Plugin.Sdk</c> themselves,
/// plus (this took a real debugging pass to find) every <c>Microsoft.Extensions.*</c>/
/// <c>Microsoft.AspNetCore.*</c>/<c>System.*</c> assembly those reference — <c>IPromptOpsPlugin
/// .Register</c>'s signature is <c>(IServiceCollection, IConfiguration)</c>, and
/// <c>Microsoft.Extensions.DependencyInjection.Abstractions</c>/<c>...Configuration.Abstractions</c>
/// are plain NuGet packages (not part of the ASP.NET Core shared framework's TPA list) that get
/// copied into every plugin's own publish output. Loading a second copy of just those two
/// assemblies is enough to break it: the plugin's <c>IServiceCollection</c> parameter type stops
/// being the same type as the host's, and the CLR fails to build the plugin type's method table
/// with a "Method 'Register' does not have an implementation" <see cref="TypeLoadException"/> —
/// misleading, because the method is right there in source; the *type it takes* just doesn't match
/// across the two copies of the assembly that declares it. <see cref="Load"/> hands back the exact
/// <see cref="Assembly"/> instance already loaded in <see cref="AssemblyLoadContext.Default"/> for
/// every shared name, so identity is shared rather than merely same-named. Only a plugin's
/// genuinely private dependencies (found via <see cref="AssemblyDependencyResolver"/>, which reads
/// the plugin's own <c>.deps.json</c>) get loaded in isolation.
/// </summary>
internal sealed class PluginLoadContext : AssemblyLoadContext
{
    private static readonly string[] SharedAssemblyPrefixes =
    [
        "PromptOps.Domain",
        "PromptOps.Application",
        "PromptOps.Plugin.Sdk",
        "Microsoft.Extensions.",
        "Microsoft.AspNetCore",
        "System."
    ];

    private readonly AssemblyDependencyResolver _resolver;

    public PluginLoadContext(string pluginDllPath)
        : base(name: Path.GetFileNameWithoutExtension(pluginDllPath), isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(pluginDllPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name is not null && IsShared(assemblyName.Name))
        {
            return Default.Assemblies.FirstOrDefault(a => a.GetName().Name == assemblyName.Name);
        }

        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path is not null ? LoadFromAssemblyPath(path) : null;
    }

    private static bool IsShared(string assemblyName) =>
        SharedAssemblyPrefixes.Any(prefix => assemblyName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
}
