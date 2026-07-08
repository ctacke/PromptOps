using System.Reflection;
using NetArchTest.Rules;
using Xunit;

namespace PromptOps.Architecture.Tests;

/// <summary>
/// Enforces the layering rule from ADR-0002: dependencies point inward only. Domain and
/// Application must never reference Infrastructure, any plugin, or a concrete persistence/web
/// technology — this is what makes "core knows nothing about concrete tools" a build-time
/// guarantee instead of a convention.
/// </summary>
public class LayeringTests
{
    private static readonly Assembly DomainAssembly = PromptOps.Domain.AssemblyReference.Assembly;
    private static readonly Assembly ApplicationAssembly = PromptOps.Application.AssemblyReference.Assembly;
    private static readonly Assembly InfrastructureAssembly = PromptOps.Infrastructure.AssemblyReference.Assembly;

    [Fact]
    public void Domain_Has_No_Dependency_On_Outer_Layers_Or_Frameworks()
    {
        var result = Types.InAssembly(DomainAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "PromptOps.Application",
                "PromptOps.Infrastructure",
                "PromptOps.Plugin.Sdk",
                "Microsoft.EntityFrameworkCore",
                "Microsoft.AspNetCore")
            .GetResult();

        AssertPasses(result);
    }

    [Fact]
    public void Application_Has_No_Dependency_On_Infrastructure_Plugins_Or_Frameworks()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "PromptOps.Infrastructure",
                "PromptOps.Plugin.Sdk",
                "Microsoft.EntityFrameworkCore",
                "Microsoft.AspNetCore")
            .GetResult();

        AssertPasses(result);
    }

    [Fact]
    public void Infrastructure_Has_No_Dependency_On_Plugin_Sdk()
    {
        // Plugin discovery/loading is a Host-level concern (ADR-0004) — Infrastructure only
        // implements Application's ports, it doesn't know how plugins get loaded.
        var result = Types.InAssembly(InfrastructureAssembly)
            .ShouldNot()
            .HaveDependencyOn("PromptOps.Plugin.Sdk")
            .GetResult();

        AssertPasses(result);
    }

    private static void AssertPasses(TestResult result)
    {
        Assert.True(
            result.IsSuccessful,
            $"Layering violation. Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }
}
