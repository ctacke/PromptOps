using PromptOps.Domain.Evaluations;
using Xunit;

namespace PromptOps.Domain.Tests.Evaluations;

public class AIEvaluationTests
{
    private static AIEvaluation ValidEvaluation(Guid? executionId = null) => AIEvaluation.Record(
        executionId ?? Guid.NewGuid(), "ai-judge", "claude-sonnet-5",
        satisfiesAcceptanceCriteria: true,
        adrViolations: ["ADR-0002: layering violated"],
        ignoredRequirements: [],
        unnecessaryComplexityNotes: "added an unused abstraction",
        suggestedPromptImprovements: ["be more specific about the target file"],
        rawResponse: "{\"satisfiesAcceptanceCriteria\":true}");

    [Fact]
    public void Record_Rejects_Empty_ExecutionId()
    {
        Assert.Throws<ArgumentException>(() => AIEvaluation.Record(
            Guid.Empty, "ai-judge", null, true, [], [], null, [], "{}"));
    }

    [Fact]
    public void Record_Rejects_Empty_JudgeProviderId()
    {
        Assert.Throws<ArgumentException>(() => AIEvaluation.Record(
            Guid.NewGuid(), " ", null, true, [], [], null, [], "{}"));
    }

    [Fact]
    public void Record_Sets_Fields_And_Defaults_Timestamp_To_Now()
    {
        var executionId = Guid.NewGuid();
        var before = DateTimeOffset.UtcNow;

        var evaluation = ValidEvaluation(executionId);

        Assert.Equal(executionId, evaluation.ExecutionId);
        Assert.Equal("ai-judge", evaluation.JudgeProviderId);
        Assert.Equal("claude-sonnet-5", evaluation.JudgeModel);
        Assert.True(evaluation.SatisfiesAcceptanceCriteria);
        Assert.Single(evaluation.AdrViolations);
        Assert.Empty(evaluation.IgnoredRequirements);
        Assert.InRange(evaluation.Timestamp, before, DateTimeOffset.UtcNow);
    }

    [Fact]
    public void Record_Allows_Null_SatisfiesAcceptanceCriteria_When_Judge_Has_No_Opinion()
    {
        var evaluation = AIEvaluation.Record(
            Guid.NewGuid(), "ai-judge", null, satisfiesAcceptanceCriteria: null,
            adrViolations: [], ignoredRequirements: [], unnecessaryComplexityNotes: null,
            suggestedPromptImprovements: [], rawResponse: "{}");

        Assert.Null(evaluation.SatisfiesAcceptanceCriteria);
    }

    [Fact]
    public void Record_Raises_AIEvaluationRecorded()
    {
        var executionId = Guid.NewGuid();

        var evaluation = ValidEvaluation(executionId);

        var domainEvent = Assert.Single(evaluation.DomainEvents);
        var recorded = Assert.IsType<AIEvaluationRecorded>(domainEvent);
        Assert.Equal(evaluation.Id, recorded.EvaluationId);
        Assert.Equal(executionId, recorded.ExecutionId);
        Assert.Equal("ai-judge", recorded.JudgeProviderId);
    }

    [Fact]
    public void Rehydrate_Does_Not_Raise_A_Domain_Event()
    {
        var evaluation = AIEvaluation.Rehydrate(
            Guid.NewGuid(), Guid.NewGuid(), "ai-judge", "claude-sonnet-5", true,
            ["ADR-0001"], [], "notes", ["suggestion"], "{}", DateTimeOffset.UtcNow);

        Assert.Empty(evaluation.DomainEvents);
    }
}
