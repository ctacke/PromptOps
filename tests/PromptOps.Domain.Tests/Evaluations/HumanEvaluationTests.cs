using PromptOps.Domain.Evaluations;
using Xunit;

namespace PromptOps.Domain.Tests.Evaluations;

public class HumanEvaluationTests
{
    private static HumanEvaluation ValidEvaluation(Guid? executionId = null) => HumanEvaluation.Submit(
        executionId ?? Guid.NewGuid(), "alice@example.com",
        correctness: 5, helpfulness: 4, architecture: 3, readability: 5, completeness: 4,
        hallucinations: false, confidence: 5, overallSatisfaction: 4, notes: "good");

    [Fact]
    public void Submit_Rejects_Empty_ExecutionId()
    {
        Assert.Throws<ArgumentException>(() => HumanEvaluation.Submit(
            Guid.Empty, "alice", 5, 5, 5, 5, 5, false, 5, 5));
    }

    [Fact]
    public void Submit_Rejects_Empty_EvaluatorId()
    {
        Assert.Throws<ArgumentException>(() => HumanEvaluation.Submit(
            Guid.NewGuid(), " ", 5, 5, 5, 5, 5, false, 5, 5));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    public void Submit_Rejects_Out_Of_Range_Correctness(int invalidRating)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => HumanEvaluation.Submit(
            Guid.NewGuid(), "alice", invalidRating, 5, 5, 5, 5, false, 5, 5));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public void Submit_Accepts_Boundary_Ratings(int boundaryRating)
    {
        var evaluation = HumanEvaluation.Submit(
            Guid.NewGuid(), "alice", boundaryRating, boundaryRating, boundaryRating,
            boundaryRating, boundaryRating, false, boundaryRating, boundaryRating);

        Assert.Equal(boundaryRating, evaluation.Correctness);
        Assert.Equal(boundaryRating, evaluation.OverallSatisfaction);
    }

    [Fact]
    public void Submit_Sets_Fields_And_Defaults_Timestamp_To_Now()
    {
        var executionId = Guid.NewGuid();
        var before = DateTimeOffset.UtcNow;

        var evaluation = ValidEvaluation(executionId);

        Assert.Equal(executionId, evaluation.ExecutionId);
        Assert.Equal("alice@example.com", evaluation.EvaluatorId);
        Assert.False(evaluation.Hallucinations);
        Assert.Equal("good", evaluation.Notes);
        Assert.InRange(evaluation.Timestamp, before, DateTimeOffset.UtcNow);
    }

    [Fact]
    public void Submit_Raises_HumanEvaluationSubmitted()
    {
        var executionId = Guid.NewGuid();

        var evaluation = ValidEvaluation(executionId);

        var domainEvent = Assert.Single(evaluation.DomainEvents);
        var submitted = Assert.IsType<HumanEvaluationSubmitted>(domainEvent);
        Assert.Equal(evaluation.Id, submitted.EvaluationId);
        Assert.Equal(executionId, submitted.ExecutionId);
        Assert.Equal("alice@example.com", submitted.EvaluatorId);
    }

    [Fact]
    public void Rehydrate_Does_Not_Raise_A_Domain_Event()
    {
        var evaluation = HumanEvaluation.Rehydrate(
            Guid.NewGuid(), Guid.NewGuid(), "alice", 5, 4, 3, 5, 4, false, 5, 4, "notes", DateTimeOffset.UtcNow);

        Assert.Empty(evaluation.DomainEvents);
    }
}
