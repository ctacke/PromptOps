using PromptOps.Application.Providers;
using PromptOps.Infrastructure.Providers;
using Xunit;

namespace PromptOps.Infrastructure.Tests;

/// <summary>
/// Unit tests for Phase 16b's <see cref="AIPromptBenchmarkProvider"/> — scenario generation, running
/// both prompts, grading, and averaging. The stub execution provider recognizes each call by a
/// distinctive marker in the prompt (the same markers the real prompts use).
/// </summary>
public class AIPromptBenchmarkProviderTests
{
    [Fact]
    public async Task Averages_Graded_Scores_Across_Generated_Scenarios()
    {
        var stub = new ScriptedExecutionProvider(
            scenariosJson: """["scenario one", "scenario two"]""",
            gradeJson: """{"activeScore": 60, "candidateScore": 90}""");
        var provider = new AIPromptBenchmarkProvider(stub);

        var result = await provider.CompareAsync("active", "candidate", ["debugging"], sampleSize: 2);

        Assert.NotNull(result);
        Assert.Equal(60, result!.ActiveScore);
        Assert.Equal(90, result.CandidateScore);
        Assert.Equal(2, result.SampleSize); // both scenarios graded
    }

    [Fact]
    public async Task Returns_Null_When_No_Scenarios_Can_Be_Generated()
    {
        // The no-op ManualAIExecutionProvider behaves like this: empty output → no scenarios.
        var stub = new ScriptedExecutionProvider(scenariosJson: "", gradeJson: "");
        var provider = new AIPromptBenchmarkProvider(stub);

        var result = await provider.CompareAsync("active", "candidate", ["debugging"], sampleSize: 3);

        Assert.Null(result);
    }

    [Fact]
    public async Task Returns_Null_When_Sample_Size_Is_Zero()
    {
        var stub = new ScriptedExecutionProvider(scenariosJson: """["x"]""", gradeJson: """{"activeScore":1,"candidateScore":2}""");
        var provider = new AIPromptBenchmarkProvider(stub);

        var result = await provider.CompareAsync("active", "candidate", [], sampleSize: 0);

        Assert.Null(result);
        Assert.Equal(0, stub.CallCount); // short-circuits before any model call
    }

    /// <summary>Returns scenario JSON for the generation call, grade JSON for the grading call, and plain output when running a prompt on a scenario — distinguished by markers in the real prompts.</summary>
    private sealed class ScriptedExecutionProvider(string scenariosJson, string gradeJson) : IAIExecutionProvider
    {
        public string Name => "scripted";
        public int CallCount { get; private set; }

        public Task<string> ExecuteAsync(string promptContent, IReadOnlyDictionary<string, string> inputs, CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (promptContent.Contains("Generate realistic task scenarios", StringComparison.Ordinal))
                return Task.FromResult(scenariosJson);
            if (promptContent.Contains("Score both outputs", StringComparison.Ordinal))
                return Task.FromResult(gradeJson);
            return Task.FromResult("some output");
        }
    }
}
