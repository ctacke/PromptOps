using System.Reflection;

namespace PromptOps.Infrastructure;

/// <summary>Marker type used by tests to reference this assembly without depending on a specific infrastructure type.</summary>
public static class AssemblyReference
{
    public static readonly Assembly Assembly = typeof(AssemblyReference).Assembly;
}
