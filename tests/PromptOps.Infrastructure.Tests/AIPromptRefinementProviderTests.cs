using PromptOps.Application.Providers;
using PromptOps.Infrastructure.Providers;
using Xunit;

namespace PromptOps.Infrastructure.Tests;

/// <summary>Unit tests for Phase 16a's <see cref="AIPromptRefinementProvider"/> — it builds a refinement prompt, calls the execution provider, and cleans the output (trims, strips a stray markdown fence).</summary>
public class AIPromptRefinementProviderTests
{
    [Fact]
    public async Task Returns_The_Model_Output_Trimmed()
    {
        var provider = new AIPromptRefinementProvider(new StubExecutionProvider("  Improved prompt text.  "));

        var result = await provider.RefineAsync("original", ["do better"]);

        Assert.Equal("Improved prompt text.", result);
    }

    [Fact]
    public async Task Strips_A_Surrounding_Markdown_Fence_The_Model_Added_Anyway()
    {
        var fenced = "```\nImproved prompt text.\n```";
        var provider = new AIPromptRefinementProvider(new StubExecutionProvider(fenced));

        var result = await provider.RefineAsync("original", ["do better"]);

        Assert.Equal("Improved prompt text.", result);
    }

    [Fact]
    public async Task Passes_The_Current_Content_And_Suggestions_Into_The_Prompt()
    {
        var stub = new StubExecutionProvider("out");
        var provider = new AIPromptRefinementProvider(stub);

        await provider.RefineAsync("THE-CURRENT-CONTENT", ["FIRST-SUGGESTION", "SECOND-SUGGESTION"]);

        Assert.Contains("THE-CURRENT-CONTENT", stub.LastPrompt);
        Assert.Contains("1. FIRST-SUGGESTION", stub.LastPrompt);
        Assert.Contains("2. SECOND-SUGGESTION", stub.LastPrompt);
    }

    private sealed class StubExecutionProvider(string output) : IAIExecutionProvider
    {
        public string Name => "stub";
        public string LastPrompt { get; private set; } = "";

        public Task<string> ExecuteAsync(string promptContent, IReadOnlyDictionary<string, string> inputs, CancellationToken cancellationToken = default)
        {
            LastPrompt = promptContent;
            return Task.FromResult(output);
        }
    }
}
