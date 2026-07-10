using PromptOps.Application.Evaluations;
using PromptOps.Domain.Executions;

namespace PromptOps.Infrastructure.Tests;

public class JudgePromptBuilderTests
{
    private static ExecutionRecord SeededExecution(string[]? acceptanceCriteria = null, string[]? referencedADRs = null)
    {
        var execution = ExecutionRecord.Start(
            Guid.NewGuid(), "alice",
            new DevelopmentContext
            {
                Repository = "github.com/ctacke/PromptOps",
                AcceptanceCriteria = acceptanceCriteria ?? [],
                ReferencedADRs = referencedADRs ?? []
            });
        execution.Finish("the diff", TimeSpan.FromSeconds(1), "manual", null, null, ["a.cs"], 5, 1);
        return execution;
    }

    [Fact]
    public void Build_includes_repository_and_output()
    {
        var execution = SeededExecution();

        var prompt = JudgePromptBuilder.Build(execution);

        Assert.Contains("github.com/ctacke/PromptOps", prompt);
        Assert.Contains("the diff", prompt);
    }

    [Fact]
    public void Build_lists_acceptance_criteria_and_ADRs_when_given()
    {
        var execution = SeededExecution(["Endpoint returns 404 for unknown ids"], ["ADR-0002"]);

        var prompt = JudgePromptBuilder.Build(execution);

        Assert.Contains("Endpoint returns 404 for unknown ids", prompt);
        Assert.Contains("ADR-0002", prompt);
    }

    [Fact]
    public void Build_reports_none_given_when_empty()
    {
        var execution = SeededExecution();

        var prompt = JudgePromptBuilder.Build(execution);

        Assert.Contains("(none given)", prompt);
    }

    [Fact]
    public void AppendCorrection_includes_the_invalid_response_and_the_parse_error()
    {
        var corrected = JudgePromptBuilder.AppendCorrection("original prompt", "not json", new FormatException("boom"));

        Assert.Contains("original prompt", corrected);
        Assert.Contains("not json", corrected);
        Assert.Contains("boom", corrected);
    }
}
